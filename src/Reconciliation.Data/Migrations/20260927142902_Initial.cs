using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reconciliation.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "import_file",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FileName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    RowsRead = table.Column<long>(type: "bigint", nullable: false),
                    RowsImported = table.Column<long>(type: "bigint", nullable: false),
                    RowsSkipped = table.Column<long>(type: "bigint", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FinishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_file", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "rejected_row",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourceFile = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    LineNumber = table.Column<long>(type: "bigint", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RawText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RejectedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rejected_row", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "transaction_record",
                columns: table => new
                {
                    txn_id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    account = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    value_date = table.Column<DateOnly>(type: "date", nullable: false),
                    counterparty = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    source_file = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    imported_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transaction_record", x => x.txn_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_import_file_Sha256",
                table: "import_file",
                column: "Sha256",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rejected_row_SourceFile_Reason",
                table: "rejected_row",
                columns: new[] { "SourceFile", "Reason" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "import_file");

            migrationBuilder.DropTable(
                name: "rejected_row");

            migrationBuilder.DropTable(
                name: "transaction_record");
        }
    }
}
