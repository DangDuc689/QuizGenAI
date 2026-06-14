using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuizGenAI.Migrations
{
    /// <inheritdoc />
    public partial class AddLockedAtToApplicationUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LockedAt",
                table: "AspNetUsers",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LockedAt",
                table: "AspNetUsers");
        }
    }
}
