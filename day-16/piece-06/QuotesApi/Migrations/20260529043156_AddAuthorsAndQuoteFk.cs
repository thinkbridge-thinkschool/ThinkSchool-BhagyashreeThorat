using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuotesApi.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthorsAndQuoteFk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AuthorId",
                table: "Quotes",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Authors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Authors", x => x.Id);
                });

            // DELIBERATELY OMITTED for the Day-11 performance lab.
            // EF Core scaffolds a nonclustered index on every FK column; that line
            // (CreateIndex IX_Quotes_AuthorId) was removed by hand so the FK exists
            // WITHOUT a supporting index. Result: the per-author lookups in the slow
            // /api/authors/summary endpoint each do a Clustered Index Scan of Quotes.
            // The covering index is added later by Scripts/add-covering-index.sql.

            migrationBuilder.AddForeignKey(
                name: "FK_Quotes_Authors_AuthorId",
                table: "Quotes",
                column: "AuthorId",
                principalTable: "Authors",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Quotes_Authors_AuthorId",
                table: "Quotes");

            migrationBuilder.DropTable(
                name: "Authors");

            // IX_Quotes_AuthorId intentionally not created in Up, so nothing to drop.

            migrationBuilder.DropColumn(
                name: "AuthorId",
                table: "Quotes");
        }
    }
}
