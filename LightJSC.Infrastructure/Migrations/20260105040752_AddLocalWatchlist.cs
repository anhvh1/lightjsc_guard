using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LightJSC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLocalWatchlist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "face_templates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    FeatureBytes = table.Column<byte[]>(type: "bytea", nullable: false),
                    L2Norm = table.Column<float>(type: "real", nullable: false),
                    FeatureVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    FaceImageJpeg = table.Column<byte[]>(type: "bytea", nullable: true),
                    SourceCameraId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    FeatureHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_face_templates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "persons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FirstName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LastName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Gender = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Age = table.Column<int>(type: "integer", nullable: true),
                    Remarks = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_persons", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_face_templates_FeatureHash",
                table: "face_templates",
                column: "FeatureHash");

            migrationBuilder.CreateIndex(
                name: "IX_face_templates_IsActive",
                table: "face_templates",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_face_templates_PersonId",
                table: "face_templates",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_face_templates_UpdatedAt",
                table: "face_templates",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_persons_Code",
                table: "persons",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_persons_IsActive",
                table: "persons",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_persons_UpdatedAt",
                table: "persons",
                column: "UpdatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "face_templates");

            migrationBuilder.DropTable(
                name: "persons");
        }
    }
}

