using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LightJSC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonScanFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "persons",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "DateOfBirth",
                table: "persons",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "DateOfIssue",
                table: "persons",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentNumber",
                table: "persons",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PersonalId",
                table: "persons",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RawQrPayload",
                table: "persons",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_persons_DocumentNumber",
                table: "persons",
                column: "DocumentNumber");

            migrationBuilder.CreateIndex(
                name: "IX_persons_PersonalId",
                table: "persons",
                column: "PersonalId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_persons_DocumentNumber",
                table: "persons");

            migrationBuilder.DropIndex(
                name: "IX_persons_PersonalId",
                table: "persons");

            migrationBuilder.DropColumn(
                name: "Address",
                table: "persons");

            migrationBuilder.DropColumn(
                name: "DateOfBirth",
                table: "persons");

            migrationBuilder.DropColumn(
                name: "DateOfIssue",
                table: "persons");

            migrationBuilder.DropColumn(
                name: "DocumentNumber",
                table: "persons");

            migrationBuilder.DropColumn(
                name: "PersonalId",
                table: "persons");

            migrationBuilder.DropColumn(
                name: "RawQrPayload",
                table: "persons");
        }
    }
}
