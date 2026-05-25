using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ViaReservaERP.Migrations
{
    public partial class AddEnterpriseInquiries : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[EnterpriseInquiries]', N'U') IS NULL
BEGIN
    CREATE TABLE [EnterpriseInquiries] (
        [EnterpriseInquiryId] INT IDENTITY(1,1) NOT NULL,
        [CompanyName] NVARCHAR(150) NOT NULL,
        [ContactPerson] NVARCHAR(150) NOT NULL,
        [Email] NVARCHAR(150) NOT NULL,
        [Phone] NVARCHAR(50) NOT NULL,
        [NumberOfBranches] INT NOT NULL,
        [Requirements] NVARCHAR(2000) NOT NULL,
        [CustomWorkflowNeeds] NVARCHAR(2000) NULL,
        [Status] NVARCHAR(50) NOT NULL CONSTRAINT [DF_EnterpriseInquiries_Status] DEFAULT ('New'),
        [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_EnterpriseInquiries_CreatedAt] DEFAULT (GETDATE()),
        [IsDeleted] BIT NOT NULL CONSTRAINT [DF_EnterpriseInquiries_IsDeleted] DEFAULT (0),
        [DeletedAt] DATETIME2 NULL,
        [DeletedBy] INT NULL,
        CONSTRAINT [PK_EnterpriseInquiries] PRIMARY KEY ([EnterpriseInquiryId])
    );
END
");

            migrationBuilder.Sql("IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[EnterpriseInquiries]') AND name = 'IsDeleted') ALTER TABLE [EnterpriseInquiries] ADD [IsDeleted] BIT NOT NULL DEFAULT 0;");
            migrationBuilder.Sql("IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[EnterpriseInquiries]') AND name = 'CreatedAt') ALTER TABLE [EnterpriseInquiries] ADD [CreatedAt] DATETIME2 NOT NULL DEFAULT GETDATE();");
            migrationBuilder.Sql("IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[EnterpriseInquiries]') AND name = 'Status') ALTER TABLE [EnterpriseInquiries] ADD [Status] NVARCHAR(50) NOT NULL DEFAULT 'New';");
            migrationBuilder.Sql("IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[EnterpriseInquiries]') AND name = 'DeletedAt') ALTER TABLE [EnterpriseInquiries] ADD [DeletedAt] DATETIME2 NULL;");
            migrationBuilder.Sql("IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[EnterpriseInquiries]') AND name = 'DeletedBy') ALTER TABLE [EnterpriseInquiries] ADD [DeletedBy] INT NULL;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
