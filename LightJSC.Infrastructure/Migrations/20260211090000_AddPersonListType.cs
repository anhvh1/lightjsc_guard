using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LightJSC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonListType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ListType",
                table: "persons",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ListType",
                table: "persons");
        }
    }
}
