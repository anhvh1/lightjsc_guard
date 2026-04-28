using LightJSC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LightJSC.Infrastructure.Migrations
{
    [DbContext(typeof(IngestorDbContext))]
    [Migration("20260111100000_AddMapCameraFovDegrees")]
    public partial class AddMapCameraFovDegrees : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<float>(
                name: "fov_degrees",
                table: "map_camera_positions",
                type: "real",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "fov_degrees",
                table: "map_camera_positions");
        }
    }
}
