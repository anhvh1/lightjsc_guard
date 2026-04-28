using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LightJSC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCameraSeriesModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "camera_model",
                table: "cameras",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "camera_series",
                table: "cameras",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "camera_model",
                table: "cameras");

            migrationBuilder.DropColumn(
                name: "camera_series",
                table: "cameras");
        }
    }
}
