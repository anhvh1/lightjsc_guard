using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LightJSC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMapLayouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "map_camera_positions",
                columns: table => new
                {
                    map_id = table.Column<Guid>(type: "uuid", nullable: false),
                    camera_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    x = table.Column<float>(type: "real", nullable: true),
                    y = table.Column<float>(type: "real", nullable: true),
                    latitude = table.Column<double>(type: "double precision", nullable: true),
                    longitude = table.Column<double>(type: "double precision", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_map_camera_positions", x => new { x.map_id, x.camera_id });
                });

            migrationBuilder.CreateTable(
                name: "map_layouts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    image_path = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    image_width = table.Column<int>(type: "integer", nullable: true),
                    image_height = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_map_layouts", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_map_camera_positions_map_id",
                table: "map_camera_positions",
                column: "map_id");

            migrationBuilder.CreateIndex(
                name: "IX_map_layouts_type",
                table: "map_layouts",
                column: "type");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "map_camera_positions");

            migrationBuilder.DropTable(
                name: "map_layouts");
        }
    }
}
