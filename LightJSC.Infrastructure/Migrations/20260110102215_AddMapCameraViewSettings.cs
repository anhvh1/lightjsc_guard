using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LightJSC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMapCameraViewSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<float>(
                name: "angle_degrees",
                table: "map_camera_positions",
                type: "real",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "range_value",
                table: "map_camera_positions",
                type: "real",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "angle_degrees",
                table: "map_camera_positions");

            migrationBuilder.DropColumn(
                name: "range_value",
                table: "map_camera_positions");
        }
    }
}
