using FluentMigrator;
using Microsoft.AspNetCore.Mvc.Abstractions;

[Migration(202504260001)]
public class CreateStatsTable : Migration
{
    public override void Up()
    {
        Create.Table("Stats")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("Devices").AsInt32().WithDefaultValue(0)
            .WithColumn("Sessions").AsInt32().WithDefaultValue(0)
            .WithColumn("Messages").AsInt32().WithDefaultValue(0)
            .WithColumn("MessagesSizeBytes").AsInt64().WithDefaultValue(0)
            .WithColumn("Files").AsInt32().WithDefaultValue(0)
            .WithColumn("FilesSizeBytes").AsInt64().WithDefaultValue(0)
            .WithColumn("DateCreatedUtc").AsDateTime().WithDefault(SystemMethods.CurrentDateTime)
            ;
    }

    public override void Down()
    {
        Delete.Table("Stats");
    }
}

[Migration(202504260002)]
public class AddVersionToStats : Migration
{
    public override void Up()
    {
        Alter.Table("Stats")
            .AddColumn("Version").AsInt32().WithDefaultValue(0);
    }

    public override void Down()
    {
        Delete.Column("Version").FromTable("Stats");
    }
}

[Migration(202504260003)]
public class AddKv : Migration
{
    public override void Up()
    {
        Create.Table("Kv")
            .WithColumn("Id").AsString().PrimaryKey()
            .WithColumn("DataJson").AsString().NotNullable()
            .WithColumn("DateCreated").AsDateTime().WithDefault(SystemMethods.CurrentDateTime);
    }

    public override void Down()
    {
        Delete.Table("Sessions");
    }
}

[Migration(202504260004)]
public class SessionTable : Migration
{
    public override void Up()
    {
        Create.Table("Sessions")
            .WithColumn("Id").AsString().PrimaryKey()
            .WithColumn("IdVerification").AsString().NotNullable()
            .WithColumn("DateCreated").AsDateTime().WithDefault(SystemMethods.CurrentDateTime)
            .WithColumn("IsLiteSession").AsBoolean().WithDefaultValue(false)
            .WithColumn("CleanupJobId").AsString().Nullable()
            .WithColumn("ConnectionIdsJson").AsString().Nullable();
    }

    public override void Down()
    {
        Delete.Table("Sessions");
    }
}


[Migration(202504260005)]
public class MessagesTable : Migration
{
    public override void Up()
    {
        Create.Table("Messages")
            .WithColumn("Id").AsString().PrimaryKey()
            .WithColumn("SessionId").AsString().NotNullable().ForeignKey("Sessions", "Id").Indexed()
            .WithColumn("SessionIdVerification").AsString().Nullable()
            .WithColumn("SocketConnectionId").AsString().Nullable()
            .WithColumn("EncryptionPublicKeyAlias").AsString().Nullable()
            .WithColumn("Text").AsString().NotNullable()
            .WithColumn("DateSent").AsDateTime().NotNullable().Indexed()
            .WithColumn("HasFile").AsBoolean().NotNullable()
            .WithColumn("FileSizeBytes").AsInt64().Nullable()
            .WithColumn("IsNotification").AsBoolean().NotNullable()
            .WithColumn("DateSentEpoch").AsInt32().NotNullable().Indexed();
    }

    public override void Down()
    {
        Delete.Table("Messages");
    }
}

[Migration(202512190001)]
public class AddFileNameToMessages : Migration
{
    public override void Up()
    {
        Alter.Table("Messages")
            .AddColumn("FileName").AsString().Nullable();
    }

    public override void Down()
    {
        Delete.Column("FileName").FromTable("Messages");
    }
}