using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LightJSC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMapGeoViewSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "geo_center_latitude",
                table: "map_layouts",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "geo_center_longitude",
                table: "map_layouts",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "geo_zoom",
                table: "map_layouts",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "geo_center_latitude",
                table: "map_layouts");

            migrationBuilder.DropColumn(
                name: "geo_center_longitude",
                table: "map_layouts");

            migrationBuilder.DropColumn(
                name: "geo_zoom",
                table: "map_layouts");
        }
    }
}
