using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RiffRoster.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialRelationalStore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LegacyId = table.Column<string>(type: "text", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Location = table.Column<string>(type: "text", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Events", x => x.Id);
                    table.CheckConstraint("CK_Event_Dates", "\"EndDate\" >= \"StartDate\"");
                });

            migrationBuilder.CreateTable(
                name: "ImportReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceSha256 = table.Column<string>(type: "text", nullable: false),
                    CanonicalSha256 = table.Column<string>(type: "text", nullable: false),
                    ImportedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SourceDataVersion = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportReceipts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LegacyId = table.Column<string>(type: "text", nullable: false),
                    NormalizedName = table.Column<string>(type: "text", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: false),
                    Color = table.Column<string>(type: "text", nullable: false),
                    JoinedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LoginName = table.Column<string>(type: "text", nullable: false),
                    NormalizedLogin = table.Column<string>(type: "text", nullable: true),
                    PasswordSalt = table.Column<string>(type: "text", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    LastSelectedEventId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Accounts_Events_LastSelectedEventId",
                        column: x => x.LastSelectedEventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Days",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LegacyId = table.Column<string>(type: "text", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Label = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Days", x => x.Id);
                    table.UniqueConstraint("AK_Days_Id_EventId", x => new { x.Id, x.EventId });
                    table.ForeignKey(
                        name: "FK_Days_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Stages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stages", x => x.Id);
                    table.UniqueConstraint("AK_Stages_Id_EventId", x => new { x.Id, x.EventId });
                    table.ForeignKey(
                        name: "FK_Stages_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Groups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LegacyId = table.Column<string>(type: "text", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    JoinCodeHash = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Groups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Groups_Accounts_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Subscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LegacyId = table.Column<string>(type: "text", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Endpoint = table.Column<string>(type: "text", nullable: false),
                    P256dh = table.Column<string>(type: "text", nullable: false),
                    Auth = table.Column<string>(type: "text", nullable: false),
                    LeadMinutes = table.Column<int>(type: "integer", nullable: false),
                    IncludeCrewNames = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subscriptions", x => x.Id);
                    table.CheckConstraint("CK_Subscription_Lead", "\"LeadMinutes\" IN (10,20,30,60)");
                    table.ForeignKey(
                        name: "FK_Subscriptions_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Performances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LegacyId = table.Column<string>(type: "text", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    DayId = table.Column<Guid>(type: "uuid", nullable: false),
                    StageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Artist = table.Column<string>(type: "text", nullable: false),
                    StartsAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndsAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartOffsetMinutes = table.Column<int>(type: "integer", nullable: false),
                    EndOffsetMinutes = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Performances", x => x.Id);
                    table.CheckConstraint("CK_Performance_Duration", "\"EndsAtUtc\" > \"StartsAtUtc\"");
                    table.CheckConstraint("CK_Performance_Offsets", "\"StartOffsetMinutes\" BETWEEN -840 AND 840 AND \"EndOffsetMinutes\" BETWEEN -840 AND 840");
                    table.ForeignKey(
                        name: "FK_Performances_Days_DayId_EventId",
                        columns: x => new { x.DayId, x.EventId },
                        principalTable: "Days",
                        principalColumns: new[] { "Id", "EventId" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Performances_Stages_StageId_EventId",
                        columns: x => new { x.StageId, x.EventId },
                        principalTable: "Stages",
                        principalColumns: new[] { "Id", "EventId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Memberships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    JoinedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Memberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Memberships_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Memberships_Groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Deliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PerformanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    PerformanceStartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Deliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Deliveries_Performances_PerformanceId",
                        column: x => x.PerformanceId,
                        principalTable: "Performances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Deliveries_Subscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "Subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Selections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    PerformanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Selections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Selections_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Selections_Performances_PerformanceId",
                        column: x => x.PerformanceId,
                        principalTable: "Performances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_LastSelectedEventId",
                table: "Accounts",
                column: "LastSelectedEventId");

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_LegacyId",
                table: "Accounts",
                column: "LegacyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_NormalizedLogin",
                table: "Accounts",
                column: "NormalizedLogin",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_NormalizedName",
                table: "Accounts",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Days_EventId_Date",
                table: "Days",
                columns: new[] { "EventId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Days_LegacyId",
                table: "Days",
                column: "LegacyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Deliveries_PerformanceId",
                table: "Deliveries",
                column: "PerformanceId");

            migrationBuilder.CreateIndex(
                name: "IX_Deliveries_SubscriptionId_PerformanceId_PerformanceStartsAt",
                table: "Deliveries",
                columns: new[] { "SubscriptionId", "PerformanceId", "PerformanceStartsAt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Events_LegacyId",
                table: "Events",
                column: "LegacyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Groups_LegacyId",
                table: "Groups",
                column: "LegacyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Groups_OwnerId",
                table: "Groups",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportReceipts_SourceSha256",
                table: "ImportReceipts",
                column: "SourceSha256",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Memberships_AccountId",
                table: "Memberships",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Memberships_GroupId_AccountId",
                table: "Memberships",
                columns: new[] { "GroupId", "AccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Performances_DayId_EventId",
                table: "Performances",
                columns: new[] { "DayId", "EventId" });

            migrationBuilder.CreateIndex(
                name: "IX_Performances_LegacyId",
                table: "Performances",
                column: "LegacyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Performances_StageId_EventId",
                table: "Performances",
                columns: new[] { "StageId", "EventId" });

            migrationBuilder.CreateIndex(
                name: "IX_Selections_AccountId_PerformanceId",
                table: "Selections",
                columns: new[] { "AccountId", "PerformanceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Selections_PerformanceId",
                table: "Selections",
                column: "PerformanceId");

            migrationBuilder.CreateIndex(
                name: "IX_Stages_EventId_Name",
                table: "Stages",
                columns: new[] { "EventId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_AccountId",
                table: "Subscriptions",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_Endpoint",
                table: "Subscriptions",
                column: "Endpoint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_LegacyId",
                table: "Subscriptions",
                column: "LegacyId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Deliveries");

            migrationBuilder.DropTable(
                name: "ImportReceipts");

            migrationBuilder.DropTable(
                name: "Memberships");

            migrationBuilder.DropTable(
                name: "Selections");

            migrationBuilder.DropTable(
                name: "Subscriptions");

            migrationBuilder.DropTable(
                name: "Groups");

            migrationBuilder.DropTable(
                name: "Performances");

            migrationBuilder.DropTable(
                name: "Accounts");

            migrationBuilder.DropTable(
                name: "Days");

            migrationBuilder.DropTable(
                name: "Stages");

            migrationBuilder.DropTable(
                name: "Events");
        }
    }
}
