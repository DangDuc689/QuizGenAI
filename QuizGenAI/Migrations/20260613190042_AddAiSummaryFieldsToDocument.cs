using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuizGenAI.Migrations
{
    /// <inheritdoc />
    public partial class AddAiSummaryFieldsToDocument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AiAudience",
                table: "Documents",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiKeyPoints",
                table: "Documents",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiSummary",
                table: "Documents",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AiAudience",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "AiKeyPoints",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "AiSummary",
                table: "Documents");
        }
    }
}
