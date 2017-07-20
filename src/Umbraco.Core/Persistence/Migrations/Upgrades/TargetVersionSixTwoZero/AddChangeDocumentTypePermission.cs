﻿using System.Linq;
using NPoco;
using Umbraco.Core.Configuration;
using Umbraco.Core.Logging;
using Umbraco.Core.Models.Rdbms;
using Umbraco.Core.Persistence.SqlSyntax;

namespace Umbraco.Core.Persistence.Migrations.Upgrades.TargetVersionSixTwoZero
{
    [Migration("7.1.0", 3, Constants.System.UmbracoMigrationName)]
    [Migration("6.2.0", 3, Constants.System.UmbracoMigrationName)]
    public class AddChangeDocumentTypePermission : MigrationBase
    {
        public AddChangeDocumentTypePermission(IMigrationContext context)
            : base(context)
        { }


        public override void Up()
        {
            Execute.Code(AddChangeDocumentTypePermissionDo);
        }

        public override void Down()
        {
            Execute.Code(UndoChangeDocumentTypePermissionDo);
        }

        private static string AddChangeDocumentTypePermissionDo(IDatabase database)
        {
            var adminUserType = database.Fetch<UserTypeDto>("WHERE Id = 1").FirstOrDefault();

            if (adminUserType != null)
            {
                if (adminUserType.DefaultPermissions.Contains("7") == false)
                {
                    adminUserType.DefaultPermissions = adminUserType.DefaultPermissions + "7";
                    database.Save<UserTypeDto>(adminUserType);
                }
            }

            return string.Empty;
        }

        private static string UndoChangeDocumentTypePermissionDo(IDatabase database)
        {
            var adminUserType = database.Fetch<UserTypeDto>("WHERE Id = 1").FirstOrDefault();

            if (adminUserType != null)
            {
                if (adminUserType.DefaultPermissions.Contains("7"))
                {
                    adminUserType.DefaultPermissions = adminUserType.DefaultPermissions.Replace("7", "");
                    database.Save<UserTypeDto>(adminUserType);
                }
            }

            return string.Empty;
        }
    }
}
