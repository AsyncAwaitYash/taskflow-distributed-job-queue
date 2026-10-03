using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskFlow.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Readers see the last committed row version instead of taking shared locks. Without this, a
    /// GET that joins Jobs to JobAttempts can deadlock with a worker updating the same job (error 1205).
    /// ALTER DATABASE cannot run inside a transaction, and ROLLBACK IMMEDIATE ends other open
    /// transactions on this database while the setting changes.
    /// </summary>
    public partial class EnableReadCommittedSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;",
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT OFF WITH ROLLBACK IMMEDIATE;",
                suppressTransaction: true);
        }
    }
}
