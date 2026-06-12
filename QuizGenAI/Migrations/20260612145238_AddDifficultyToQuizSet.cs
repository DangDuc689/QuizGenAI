using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuizGenAI.Migrations
{
    /// <inheritdoc />
    public partial class AddDifficultyToQuizSet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Difficulty",
                table: "QuizSets",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Difficulty",
                table: "QuizSets");
        }
    }
}
