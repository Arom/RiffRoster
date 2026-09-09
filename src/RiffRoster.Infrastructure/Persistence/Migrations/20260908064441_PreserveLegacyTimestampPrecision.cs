using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RiffRoster.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PreserveLegacyTimestampPrecision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Performance_Duration",
                table: "Performances");

            migrationBuilder.DropIndex(
                name: "IX_Deliveries_SubscriptionId_PerformanceId_PerformanceStartsAt",
                table: "Deliveries");

            migrationBuilder.AddColumn<short>(
                name: "CreatedAtTickRemainder",
                table: "Subscriptions",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<short>(
                name: "LastSeenAtTickRemainder",
                table: "Subscriptions",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<short>(
                name: "EndsAtUtcTickRemainder",
                table: "Performances",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<short>(
                name: "StartsAtUtcTickRemainder",
                table: "Performances",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<short>(
                name: "JoinedAtTickRemainder",
                table: "Memberships",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<short>(
                name: "CreatedAtTickRemainder",
                table: "Groups",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<short>(
                name: "PerformanceStartsAtTickRemainder",
                table: "Deliveries",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<short>(
                name: "SentAtTickRemainder",
                table: "Deliveries",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<short>(
                name: "JoinedAtTickRemainder",
                table: "Accounts",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Subscriptions_CreatedAtTickRemainder",
                table: "Subscriptions",
                sql: "\"CreatedAtTickRemainder\" BETWEEN 0 AND 9");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Subscriptions_LastSeenAtTickRemainder",
                table: "Subscriptions",
                sql: "\"LastSeenAtTickRemainder\" BETWEEN 0 AND 9");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Performance_Duration",
                table: "Performances",
                sql: "(\"EndsAtUtc\", \"EndsAtUtcTickRemainder\") > (\"StartsAtUtc\", \"StartsAtUtcTickRemainder\")");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Performances_EndsAtUtcTickRemainder",
                table: "Performances",
                sql: "\"EndsAtUtcTickRemainder\" BETWEEN 0 AND 9");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Performances_StartsAtUtcTickRemainder",
                table: "Performances",
                sql: "\"StartsAtUtcTickRemainder\" BETWEEN 0 AND 9");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Memberships_JoinedAtTickRemainder",
                table: "Memberships",
                sql: "\"JoinedAtTickRemainder\" BETWEEN 0 AND 9");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Groups_CreatedAtTickRemainder",
                table: "Groups",
                sql: "\"CreatedAtTickRemainder\" BETWEEN 0 AND 9");

            migrationBuilder.CreateIndex(
                name: "IX_Deliveries_SubscriptionId_PerformanceId_PerformanceStartsAt~",
                table: "Deliveries",
                columns: new[] { "SubscriptionId", "PerformanceId", "PerformanceStartsAt", "PerformanceStartsAtTickRemainder" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Deliveries_PerformanceStartsAtTickRemainder",
                table: "Deliveries",
                sql: "\"PerformanceStartsAtTickRemainder\" BETWEEN 0 AND 9");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Deliveries_SentAtTickRemainder",
                table: "Deliveries",
                sql: "\"SentAtTickRemainder\" BETWEEN 0 AND 9");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Accounts_JoinedAtTickRemainder",
                table: "Accounts",
                sql: "\"JoinedAtTickRemainder\" BETWEEN 0 AND 9");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Subscriptions_CreatedAtTickRemainder",
                table: "Subscriptions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Subscriptions_LastSeenAtTickRemainder",
                table: "Subscriptions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Performance_Duration",
                table: "Performances");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Performances_EndsAtUtcTickRemainder",
                table: "Performances");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Performances_StartsAtUtcTickRemainder",
                table: "Performances");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Memberships_JoinedAtTickRemainder",
                table: "Memberships");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Groups_CreatedAtTickRemainder",
                table: "Groups");

            migrationBuilder.DropIndex(
                name: "IX_Deliveries_SubscriptionId_PerformanceId_PerformanceStartsAt~",
                table: "Deliveries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Deliveries_PerformanceStartsAtTickRemainder",
                table: "Deliveries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Deliveries_SentAtTickRemainder",
                table: "Deliveries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Accounts_JoinedAtTickRemainder",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "CreatedAtTickRemainder",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "LastSeenAtTickRemainder",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "EndsAtUtcTickRemainder",
                table: "Performances");

            migrationBuilder.DropColumn(
                name: "StartsAtUtcTickRemainder",
                table: "Performances");

            migrationBuilder.DropColumn(
                name: "JoinedAtTickRemainder",
                table: "Memberships");

            migrationBuilder.DropColumn(
                name: "CreatedAtTickRemainder",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "PerformanceStartsAtTickRemainder",
                table: "Deliveries");

            migrationBuilder.DropColumn(
                name: "SentAtTickRemainder",
                table: "Deliveries");

            migrationBuilder.DropColumn(
                name: "JoinedAtTickRemainder",
                table: "Accounts");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Performance_Duration",
                table: "Performances",
                sql: "\"EndsAtUtc\" > \"StartsAtUtc\"");

            migrationBuilder.CreateIndex(
                name: "IX_Deliveries_SubscriptionId_PerformanceId_PerformanceStartsAt",
                table: "Deliveries",
                columns: new[] { "SubscriptionId", "PerformanceId", "PerformanceStartsAt" },
                unique: true);
        }
    }
}
