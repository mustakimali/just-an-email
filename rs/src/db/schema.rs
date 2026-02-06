use sqlx::SqlitePool;

pub async fn run_migrations(pool: &SqlitePool) -> Result<(), sqlx::Error> {
    sqlx::query(
        "CREATE TABLE IF NOT EXISTS Stats (
            Id INTEGER PRIMARY KEY,
            Devices INTEGER NOT NULL DEFAULT 0,
            Sessions INTEGER NOT NULL DEFAULT 0,
            Messages INTEGER NOT NULL DEFAULT 0,
            MessagesSizeBytes INTEGER NOT NULL DEFAULT 0,
            Files INTEGER NOT NULL DEFAULT 0,
            FilesSizeBytes INTEGER NOT NULL DEFAULT 0,
            DateCreatedUtc TEXT NOT NULL DEFAULT (datetime('now')),
            Version INTEGER NOT NULL DEFAULT 0
        )",
    )
    .execute(pool)
    .await?;

    sqlx::query(
        "CREATE TABLE IF NOT EXISTS Kv (
            Id TEXT PRIMARY KEY,
            DataJson TEXT NOT NULL,
            DateCreated TEXT NOT NULL DEFAULT (datetime('now'))
        )",
    )
    .execute(pool)
    .await?;

    sqlx::query(
        "CREATE TABLE IF NOT EXISTS Sessions (
            Id TEXT PRIMARY KEY,
            IdVerification TEXT NOT NULL,
            DateCreated TEXT NOT NULL DEFAULT (datetime('now')),
            IsLiteSession INTEGER NOT NULL DEFAULT 0,
            CleanupJobId TEXT,
            ConnectionIdsJson TEXT
        )",
    )
    .execute(pool)
    .await?;

    sqlx::query(
        "CREATE TABLE IF NOT EXISTS Messages (
            Id TEXT PRIMARY KEY,
            SessionId TEXT NOT NULL REFERENCES Sessions(Id),
            SessionIdVerification TEXT,
            SocketConnectionId TEXT,
            EncryptionPublicKeyAlias TEXT,
            Text TEXT NOT NULL,
            FileName TEXT,
            DateSent TEXT NOT NULL,
            HasFile INTEGER NOT NULL DEFAULT 0,
            FileSizeBytes INTEGER,
            IsNotification INTEGER NOT NULL DEFAULT 0,
            DateSentEpoch INTEGER NOT NULL
        )",
    )
    .execute(pool)
    .await?;

    sqlx::query("CREATE INDEX IF NOT EXISTS idx_messages_session ON Messages(SessionId)")
        .execute(pool)
        .await?;

    sqlx::query("CREATE INDEX IF NOT EXISTS idx_messages_epoch ON Messages(DateSentEpoch)")
        .execute(pool)
        .await?;

    sqlx::query(
        "CREATE TABLE IF NOT EXISTS PublicKeys (
            Id TEXT PRIMARY KEY,
            SessionId TEXT NOT NULL,
            Alias TEXT NOT NULL,
            PublicKeyJson TEXT NOT NULL,
            DateCreated TEXT NOT NULL DEFAULT (datetime('now'))
        )",
    )
    .execute(pool)
    .await?;

    sqlx::query("CREATE INDEX IF NOT EXISTS idx_publickeys_session ON PublicKeys(SessionId)")
        .execute(pool)
        .await?;

    Ok(())
}
