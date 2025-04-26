using FluentMigrator;

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
