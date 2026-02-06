use chrono::Utc;
use sqlx::SqlitePool;

use crate::models::stats::{StatMonth, StatYear, Stats};

pub async fn stats_get_id_for(year: Option<i32>, month: Option<u32>, day: Option<u32>) -> i64 {
    let Some(y) = year else { return -1 };
    let y_short = y % 100;
    let m = month.unwrap_or(0) as i64;
    let d = day.unwrap_or(0) as i64;
    y_short as i64 * 10000 + m * 100 + d
}

pub async fn stats_find_by_id_or_new(pool: &SqlitePool, id: i64) -> Result<Stats, sqlx::Error> {
    let row: Option<Stats> = sqlx::query_as("SELECT * FROM Stats WHERE Id = ?")
        .bind(id)
        .fetch_optional(pool)
        .await?;

    Ok(row.unwrap_or(Stats {
        id,
        messages: 0,
        messages_size_bytes: 0,
        files: 0,
        files_size_bytes: 0,
        devices: 0,
        sessions: 0,
        version: 0,
        date_created_utc: Utc::now().format("%Y-%m-%dT%H:%M:%S").to_string(),
    }))
}

pub async fn stats_find_by_date_or_new(
    pool: &SqlitePool,
    year: Option<i32>,
    month: Option<u32>,
    day: Option<u32>,
) -> Result<Stats, sqlx::Error> {
    let id = stats_get_id_for(year, month, day).await;
    stats_find_by_id_or_new(pool, id).await
}

async fn upsert_stats(pool: &SqlitePool, stats: &mut Stats) -> Result<(), sqlx::Error> {
    stats.version += 1;
    let result = sqlx::query(
        "INSERT INTO Stats (Id, Messages, MessagesSizeBytes, Files, FilesSizeBytes, Devices, Sessions, DateCreatedUtc, Version) \
         VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?) \
         ON CONFLICT(Id) DO UPDATE SET \
            Messages = excluded.Messages, \
            MessagesSizeBytes = excluded.MessagesSizeBytes, \
            Files = excluded.Files, \
            FilesSizeBytes = excluded.FilesSizeBytes, \
            Devices = excluded.Devices, \
            Sessions = excluded.Sessions, \
            Version = excluded.Version \
         WHERE Version = excluded.Version - 1",
    )
    .bind(stats.id)
    .bind(stats.messages)
    .bind(stats.messages_size_bytes)
    .bind(stats.files)
    .bind(stats.files_size_bytes)
    .bind(stats.devices)
    .bind(stats.sessions)
    .bind(&stats.date_created_utc)
    .bind(stats.version)
    .execute(pool)
    .await?;

    if result.rows_affected() == 0 {
        tracing::warn!("Optimistic concurrency conflict for stats id={}", stats.id);
    }
    Ok(())
}

pub enum RecordType {
    Session,
    Device,
    Message,
}

pub async fn record_stats(pool: &SqlitePool, record_type: RecordType, inc: i64) -> Result<(), sqlx::Error> {
    let now = Utc::now();
    let year = now.format("%Y").to_string().parse::<i32>().unwrap_or(2025);
    let month = now.format("%m").to_string().parse::<u32>().unwrap_or(1);
    let day = now.format("%d").to_string().parse::<u32>().unwrap_or(1);

    let mut all_time = stats_find_by_date_or_new(pool, None, None, None).await?;
    let mut this_year = stats_find_by_date_or_new(pool, Some(year), None, None).await?;
    let mut this_month = stats_find_by_date_or_new(pool, Some(year), Some(month), None).await?;
    let mut today = stats_find_by_date_or_new(pool, Some(year), Some(month), Some(day)).await?;

    let apply = |s: &mut Stats| match record_type {
        RecordType::Session => s.sessions += inc,
        RecordType::Device => s.devices += inc,
        RecordType::Message => s.messages += inc,
    };

    apply(&mut all_time);
    apply(&mut this_year);
    apply(&mut this_month);
    apply(&mut today);

    upsert_stats(pool, &mut all_time).await?;
    upsert_stats(pool, &mut this_year).await?;
    upsert_stats(pool, &mut this_month).await?;
    upsert_stats(pool, &mut today).await?;

    Ok(())
}

pub async fn record_message_stats(
    pool: &SqlitePool,
    msg_size_bytes: i64,
    file_size_bytes: Option<i64>,
) -> Result<(), sqlx::Error> {
    let now = Utc::now();
    let year = now.format("%Y").to_string().parse::<i32>().unwrap_or(2025);
    let month = now.format("%m").to_string().parse::<u32>().unwrap_or(1);
    let day = now.format("%d").to_string().parse::<u32>().unwrap_or(1);

    let mut all_time = stats_find_by_date_or_new(pool, None, None, None).await?;
    let mut this_year = stats_find_by_date_or_new(pool, Some(year), None, None).await?;
    let mut this_month = stats_find_by_date_or_new(pool, Some(year), Some(month), None).await?;
    let mut today = stats_find_by_date_or_new(pool, Some(year), Some(month), Some(day)).await?;

    let apply = |s: &mut Stats| {
        s.messages += 1;
        s.messages_size_bytes += msg_size_bytes;
        if let Some(fs) = file_size_bytes {
            s.files += 1;
            s.files_size_bytes += fs;
        }
    };

    apply(&mut all_time);
    apply(&mut this_year);
    apply(&mut this_month);
    apply(&mut today);

    upsert_stats(pool, &mut all_time).await?;
    upsert_stats(pool, &mut this_year).await?;
    upsert_stats(pool, &mut this_month).await?;
    upsert_stats(pool, &mut today).await?;

    Ok(())
}

pub async fn get_all_stats(pool: &SqlitePool) -> Result<Vec<StatYear>, sqlx::Error> {
    let all: Vec<Stats> = sqlx::query_as("SELECT * FROM Stats WHERE Id > 1")
        .fetch_all(pool)
        .await?;

    let mut years_map: std::collections::BTreeMap<String, std::collections::BTreeMap<String, Vec<Stats>>> =
        std::collections::BTreeMap::new();

    for stat in all {
        let id_str = format!("{:06}", stat.id);
        let year_key = id_str[..2].to_string();
        let month_key = id_str[2..4].to_string();

        years_map
            .entry(year_key)
            .or_default()
            .entry(month_key)
            .or_default()
            .push(stat);
    }

    let result: Vec<StatYear> = years_map
        .into_iter()
        .map(|(year, months)| StatYear {
            year,
            months: months
                .into_iter()
                .map(|(month, days)| StatMonth { month, days })
                .collect(),
        })
        .collect();

    Ok(result)
}
