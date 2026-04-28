using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LightJSC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMapHierarchyAndCameraIconScale : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "parent_id",
                table: "map_layouts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "icon_scale",
                table: "map_camera_positions",
                type: "real",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_map_layouts_parent_id",
                table: "map_layouts",
                column: "parent_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_map_layouts_parent_id",
                table: "map_layouts");

            migrationBuilder.DropColumn(
                name: "parent_id",
                table: "map_layouts");

            migrationBuilder.DropColumn(
                name: "icon_scale",
                table: "map_camera_positions");
        }
    }
}
