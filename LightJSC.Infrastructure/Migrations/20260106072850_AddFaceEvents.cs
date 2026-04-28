using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LightJSC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFaceEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "face_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventTimeUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CameraId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IsKnown = table.Column<bool>(type: "boolean", nullable: false),
                    WatchlistEntryId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    PersonId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    PersonJson = table.Column<string>(type: "jsonb", nullable: true),
                    Similarity = table.Column<float>(type: "real", nullable: true),
                    Score = table.Column<float>(type: "real", nullable: true),
                    BestshotPath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    ThumbPath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Gender = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Age = table.Column<int>(type: "integer", nullable: true),
                    Mask = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    BBoxJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_face_events", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_face_events_CameraId",
                table: "face_events",
                column: "CameraId");

            migrationBuilder.CreateIndex(
                name: "IX_face_events_EventTimeUtc",
                table: "face_events",
                column: "EventTimeUtc");

            migrationBuilder.CreateIndex(
                name: "IX_face_events_IsKnown",
                table: "face_events",
                column: "IsKnown");

            migrationBuilder.CreateIndex(
                name: "IX_face_events_PersonId",
                table: "face_events",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_face_events_WatchlistEntryId",
                table: "face_events",
                column: "WatchlistEntryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "face_events");
        }
    }
}
