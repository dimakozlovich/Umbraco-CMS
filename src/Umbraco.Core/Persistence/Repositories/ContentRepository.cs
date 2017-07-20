﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml;
using NPoco;
using Umbraco.Core.IO;
using Umbraco.Core.Logging;
using Umbraco.Core.Models;
using Umbraco.Core.Models.EntityBase;
using Umbraco.Core.Models.Membership;
using Umbraco.Core.Models.Rdbms;
using Umbraco.Core.Persistence.DatabaseModelDefinitions;
using Umbraco.Core.Persistence.Factories;
using Umbraco.Core.Persistence.Querying;
using Umbraco.Core.Cache;
using Umbraco.Core.Configuration.UmbracoSettings;
using Umbraco.Core.Persistence.Mappers;
using Umbraco.Core.Persistence.SqlSyntax;
using Umbraco.Core.Persistence.UnitOfWork;

namespace Umbraco.Core.Persistence.Repositories
{
    /// <summary>
    /// Represents a repository for doing CRUD operations for <see cref="IContent"/>.
    /// </summary>
    internal class ContentRepository : RecycleBinRepository<int, IContent, ContentRepository>, IContentRepository
    {
        private readonly IContentTypeRepository _contentTypeRepository;
        private readonly ITemplateRepository _templateRepository;
        private readonly ITagRepository _tagRepository;
        private readonly CacheHelper _cacheHelper;
        private PermissionRepository<IContent> _permissionRepository;

        public ContentRepository(IScopeUnitOfWork work, CacheHelper cacheHelper, ILogger logger, IContentTypeRepository contentTypeRepository, ITemplateRepository templateRepository, ITagRepository tagRepository /*, IContentSection contentSection*/)
            : base(work, cacheHelper, logger /*, contentSection*/)
        {
            _contentTypeRepository = contentTypeRepository ?? throw new ArgumentNullException(nameof(contentTypeRepository));
            _templateRepository = templateRepository ?? throw new ArgumentNullException(nameof(templateRepository));
            _tagRepository = tagRepository ?? throw new ArgumentNullException(nameof(tagRepository));
            _cacheHelper = cacheHelper;

            _publishedQuery =  work.Query<IContent>().Where(x => x.Published);

            EnsureUniqueNaming = true;
        }

        protected override ContentRepository This => this;

        public bool EnsureUniqueNaming { get; set; }

        // note: is ok to 'new' the repo here as it's a sub-repo really
        private PermissionRepository<IContent> PermissionRepository => _permissionRepository
            ?? (_permissionRepository = new PermissionRepository<IContent>(UnitOfWork, _cacheHelper));

        #region Overrides of RepositoryBase<IContent>

        protected override IContent PerformGet(int id)
        {
            var sql = GetBaseQuery(QueryType.Single)
                .Where(GetBaseWhereClause(), new { Id = id })
                .Where<DocumentDto>(x => x.Newest)
                .OrderByDescending<ContentVersionDto>(x => x.VersionDate);

            var dto = Database.Fetch<DocumentDto>(sql.SelectTop(1)).FirstOrDefault();

            if (dto == null)
                return null;

            var content = CreateContentFromDto(dto, dto.ContentVersionDto.VersionId);

            return content;
        }

        protected override IEnumerable<IContent> PerformGetAll(params int[] ids)
        {
            Sql<SqlContext> Translate(Sql<SqlContext> tsql)
            {
                if (ids.Any())
                    tsql.Where("umbracoNode.id in (@ids)", new { /*ids =*/ ids });

                // we only want the newest ones with this method
                tsql.Where<DocumentDto>(x => x.Newest);

                return tsql;
            }

            var sql = Translate(GetBaseQuery(QueryType.Many));
            return MapQueryDtos(Database.Fetch<DocumentDto>(sql), many: true);
        }

        protected override IEnumerable<IContent> PerformGetByQuery(IQuery<IContent> query)
        {
            var sqlClause = GetBaseQuery(QueryType.Many);
            var translator = new SqlTranslator<IContent>(sqlClause, query);
            var sql = translator.Translate()
                                .Where<DocumentDto>(x => x.Newest)
                                //.OrderByDescending<ContentVersionDto>(x => x.VersionDate)
                                .OrderBy<NodeDto>(x => x.Level)
                                .OrderBy<NodeDto>(x => x.SortOrder);

            return MapQueryDtos(Database.Fetch<DocumentDto>(sql), many: true);
        }

        #endregion

        #region Static Queries

        private readonly IQuery<IContent> _publishedQuery;

        #endregion

        #region Overrides of NPocoRepositoryBase<IContent>

        protected override Sql<SqlContext> GetBaseQuery(QueryType queryType)
        {
            var sql = Sql();

            switch (queryType)
            {
                case QueryType.Count:
                    sql = sql.SelectCount();
                    break;
                case QueryType.Ids:
                    sql = sql.Select("cmsDocument.nodeId");
                    break;
                case QueryType.Single:
                    sql = sql.Select<DocumentDto>(r =>
                                r.Select<ContentVersionDto>(rr =>
                                        rr.Select<ContentDto>(rrr =>
                                            rrr.Select<NodeDto>()))
                                 .Select<DocumentPublishedReadOnlyDto>(tableAlias: "cmsDocument2"));
                            break;
                case QueryType.Many:
                    // 'many' does not join on cmsDocument2
                    sql = sql.Select<DocumentDto>(r =>
                                r.Select<ContentVersionDto>(rr =>
                                    rr.Select<ContentDto>(rrr =>
                                        rrr.Select<NodeDto>())));
                    break;
            }

            sql
                .From<DocumentDto>()
                .InnerJoin<ContentVersionDto>()
                    .On<DocumentDto, ContentVersionDto>(left => left.VersionId, right => right.VersionId)
                .InnerJoin<ContentDto>()
                    .On<ContentVersionDto, ContentDto>(left => left.NodeId, right => right.NodeId)
                .InnerJoin<NodeDto>()
                    .On<ContentDto, NodeDto>(left => left.NodeId, right => right.NodeId);

            if (queryType == QueryType.Single)
            {
                //The only reason we apply this left outer join is to be able to pull back the DocumentPublishedReadOnlyDto
                //information with the entire data set, so basically this will get both the latest document and also it's published
                //version if it has one. When performing a count or when retrieving Ids like in paging, this is unecessary
                //and causes huge performance overhead for the SQL server, especially when sorting the result.
                //We also don't include this outer join when querying for multiple entities since it is much faster to fetch this information
                //in a separate query. For a single entity this is ok.

                var sqlx = string.Format("LEFT OUTER JOIN {0} {1} ON ({1}.{2}={0}.{2} AND {1}.{3}=1)",
                    SqlSyntax.GetQuotedTableName("cmsDocument"),
                    SqlSyntax.GetQuotedTableName("cmsDocument2"),
                    SqlSyntax.GetQuotedColumnName("nodeId"),
                    SqlSyntax.GetQuotedColumnName("published"));

                // cannot do this because NPoco does not know how to alias the table
                //.LeftOuterJoin<DocumentPublishedReadOnlyDto>()
                //.On<DocumentDto, DocumentPublishedReadOnlyDto>(left => left.NodeId, right => right.NodeId)
                // so have to rely on writing our own SQL
                sql
                    .Append(sqlx /*, new { @published = true }*/);
            }

            sql
                .Where<NodeDto>(x => x.NodeObjectType == NodeObjectTypeId);

            return sql;
        }

        // fixme - move that one up to Versionable!
        protected override Sql<SqlContext> GetBaseQuery(bool isCount)
        {
            return GetBaseQuery(isCount ? QueryType.Count : QueryType.Single);
        }

        protected override string GetBaseWhereClause()
        {
            return "umbracoNode.id = @Id";
        }

        protected override IEnumerable<string> GetDeleteClauses()
        {
            var list = new List<string>
                           {
                               "DELETE FROM umbracoRedirectUrl WHERE contentKey IN (SELECT uniqueID FROM umbracoNode WHERE id = @Id)",
                               "DELETE FROM cmsTask WHERE nodeId = @Id",
                               "DELETE FROM umbracoUser2NodeNotify WHERE nodeId = @Id",
                               "DELETE FROM umbracoUser2NodePermission WHERE nodeId = @Id",
                               "DELETE FROM umbracoRelation WHERE parentId = @Id",
                               "DELETE FROM umbracoRelation WHERE childId = @Id",
                               "DELETE FROM cmsTagRelationship WHERE nodeId = @Id",
                               "DELETE FROM umbracoDomains WHERE domainRootStructureID = @Id",
                               "DELETE FROM cmsDocument WHERE nodeId = @Id",
                               "DELETE FROM cmsPropertyData WHERE contentNodeId = @Id",
                               "DELETE FROM cmsPreviewXml WHERE nodeId = @Id",
                               "DELETE FROM cmsContentVersion WHERE ContentId = @Id",
                               "DELETE FROM cmsContentXml WHERE nodeId = @Id",
                               "DELETE FROM cmsContent WHERE nodeId = @Id",
                               "DELETE FROM umbracoAccess WHERE nodeId = @Id",
                               "DELETE FROM umbracoNode WHERE id = @Id"
                           };
            return list;
        }

        protected override Guid NodeObjectTypeId => new Guid(Constants.ObjectTypes.Document);

        #endregion

        #region Overrides of VersionableRepositoryBase<IContent>

        public override IContent GetByVersion(Guid versionId)
        {
            var sql = GetBaseQuery(false);
            sql.Where("cmsContentVersion.VersionId = @VersionId", new { VersionId = versionId });
            sql.OrderByDescending<ContentVersionDto>(x => x.VersionDate);

            var dto = Database.Fetch<DocumentDto>(sql).FirstOrDefault();

            if (dto == null)
                return null;

            var content = CreateContentFromDto(dto, versionId);

            return content;
        }

        public override void DeleteVersion(Guid versionId)
        {
            var sql = Sql()
                .SelectAll()
                .From<DocumentDto>()
                .InnerJoin<ContentVersionDto>().On<ContentVersionDto, DocumentDto>(left => left.VersionId, right => right.VersionId)
                .Where<ContentVersionDto>(x => x.VersionId == versionId)
                .Where<DocumentDto>(x => x.Newest != true);
            var dto = Database.Fetch<DocumentDto>(sql).FirstOrDefault();

            if (dto == null) return;

            PerformDeleteVersion(dto.NodeId, versionId);
        }

        public override void DeleteVersions(int id, DateTime versionDate)
        {
            var sql = Sql()
                .SelectAll()
                .From<DocumentDto>()
                .InnerJoin<ContentVersionDto>().On<ContentVersionDto, DocumentDto>(left => left.VersionId, right => right.VersionId)
                .Where<ContentVersionDto>(x => x.NodeId == id)
                .Where<ContentVersionDto>(x => x.VersionDate < versionDate)
                .Where<DocumentDto>(x => x.Newest != true);
            var list = Database.Fetch<DocumentDto>(sql);
            if (list.Any() == false) return;

            foreach (var dto in list)
            {
                PerformDeleteVersion(id, dto.VersionId);
            }
        }

        protected override void PerformDeleteVersion(int id, Guid versionId)
        {
            // raise event first else potential FK issues
            OnUowRemovingVersion(new UnitOfWorkVersionEventArgs(UnitOfWork, id, versionId));

            Database.Delete<PropertyDataDto>("WHERE contentNodeId = @Id AND versionId = @VersionId", new { Id = id, VersionId = versionId });
            Database.Delete<ContentVersionDto>("WHERE ContentId = @Id AND VersionId = @VersionId", new { Id = id, VersionId = versionId });
            Database.Delete<DocumentDto>("WHERE nodeId = @Id AND versionId = @VersionId", new { Id = id, VersionId = versionId });
        }

        #endregion

        #region Unit of Work Implementation

        protected override void PersistDeletedItem(IContent entity)
        {
            // raise event first else potential FK issues
            OnUowRemovingEntity(new UnitOfWorkEntityEventArgs(UnitOfWork, entity));

            //We need to clear out all access rules but we need to do this in a manual way since
            // nothing in that table is joined to a content id
            var subQuery = Sql()
                .Select("umbracoAccessRule.accessId")
                .From<AccessRuleDto>()
                .InnerJoin<AccessDto>()
                .On<AccessRuleDto, AccessDto>(left => left.AccessId, right => right.Id)
                .Where<AccessDto>(dto => dto.NodeId == entity.Id);
            Database.Execute(SqlSyntax.GetDeleteSubquery("umbracoAccessRule", "accessId", subQuery));

            //now let the normal delete clauses take care of everything else
            base.PersistDeletedItem(entity);
        }

        protected override void PersistNewItem(IContent entity)
        {
            ((Content)entity).AddingEntity();

            //ensure the default template is assigned
            if (entity.Template == null)
                entity.Template = entity.ContentType.DefaultTemplate;

            //Ensure unique name on the same level
            entity.Name = EnsureUniqueNodeName(entity.ParentId, entity.Name);

            //Ensure that strings don't contain characters that are invalid in XML
            entity.SanitizeEntityPropertiesForXmlStorage();

            var factory = new ContentFactory(NodeObjectTypeId, entity.Id);
            var dto = factory.BuildDto(entity);

            //NOTE Should the logic below have some kind of fallback for empty parent ids ?
            //Logic for setting Path, Level and SortOrder
            var parent = Database.First<NodeDto>("WHERE id = @ParentId", new { /*ParentId =*/ entity.ParentId });
            var level = parent.Level + 1;
            var maxSortOrder = Database.ExecuteScalar<int>(
                "SELECT coalesce(max(sortOrder),-1) FROM umbracoNode WHERE parentId = @ParentId AND nodeObjectType = @NodeObjectType",
                new { /*ParentId =*/ entity.ParentId, NodeObjectType = NodeObjectTypeId });
            var sortOrder = maxSortOrder + 1;

            //Create the (base) node data - umbracoNode
            var nodeDto = dto.ContentVersionDto.ContentDto.NodeDto;
            nodeDto.Path = parent.Path;
            nodeDto.Level = short.Parse(level.ToString(CultureInfo.InvariantCulture));
            nodeDto.SortOrder = sortOrder;
            var unused = Database.IsNew(nodeDto) ? Convert.ToInt32(Database.Insert(nodeDto)) : Database.Update(nodeDto);

            //Update with new correct path
            nodeDto.Path = string.Concat(parent.Path, ",", nodeDto.NodeId);
            nodeDto.ValidatePathWithException();
            Database.Update(nodeDto);

            //Update entity with correct values
            entity.Id = nodeDto.NodeId; //Set Id on entity to ensure an Id is set
            entity.Path = nodeDto.Path;
            entity.SortOrder = sortOrder;
            entity.Level = level;

            //Assign the same permissions to it as the parent node
            // http://issues.umbraco.org/issue/U4-2161
            var parentPermissions = PermissionRepository.GetPermissionsForEntity(entity.ParentId).ToArray();
            //if there are parent permissions then assign them, otherwise leave null and permissions will become the
            // user's default permissions.
            if (parentPermissions.Any())
            {
                var userPermissions = (
                    from perm in parentPermissions
                    from p in perm.AssignedPermissions
                    select new EntityPermissionSet.UserPermission(perm.UserId, p)).ToList();

                PermissionRepository.ReplaceEntityPermissions(new EntityPermissionSet(entity.Id, userPermissions));
                //flag the entity's permissions changed flag so we can track those changes.
                //Currently only used for the cache refreshers to detect if we should refresh all user permissions cache.
                ((Content)entity).PermissionsChanged = true;
            }

            //Create the Content specific data - cmsContent
            var contentDto = dto.ContentVersionDto.ContentDto;
            contentDto.NodeId = nodeDto.NodeId;
            Database.Insert(contentDto);

            //Create the first version - cmsContentVersion
            //Assumes a new Version guid and Version date (modified date) has been set
            var contentVersionDto = dto.ContentVersionDto;
            contentVersionDto.NodeId = nodeDto.NodeId;
            Database.Insert(contentVersionDto);

            //Create the Document specific data for this version - cmsDocument
            //Assumes a new Version guid has been generated
            dto.NodeId = nodeDto.NodeId;
            Database.Insert(dto);

            //Create the PropertyData for this version - cmsPropertyData
            var propertyFactory = new PropertyFactory(entity.ContentType.CompositionPropertyTypes.ToArray(), entity.Version, entity.Id);
            var propertyDataDtos = propertyFactory.BuildDto(entity.Properties);
            var keyDictionary = new Dictionary<int, int>();

            //Add Properties
            foreach (var propertyDataDto in propertyDataDtos)
            {
                var primaryKey = Convert.ToInt32(Database.Insert(propertyDataDto));
                keyDictionary.Add(propertyDataDto.PropertyTypeId, primaryKey);
            }

            //Update Properties with its newly set Id
            foreach (var property in entity.Properties)
                property.Id = keyDictionary[property.PropertyTypeId];

            //lastly, check if we are a creating a published version , then update the tags table
            if (entity.Published)
                UpdateEntityTags(entity, _tagRepository);

            // published => update published version infos, else leave it blank
            if (entity.Published)
            {
                dto.DocumentPublishedReadOnlyDto = new DocumentPublishedReadOnlyDto
                {
                    VersionId = dto.VersionId,
                    VersionDate = dto.UpdateDate,
                    Newest = true,
                    NodeId = dto.NodeId,
                    Published = true
                };
                ((Content) entity).PublishedVersionGuid = dto.VersionId;
                ((Content) entity).PublishedDate = dto.UpdateDate;
            }

            OnUowRefreshedEntity(new UnitOfWorkEntityEventArgs(UnitOfWork, entity));

            entity.ResetDirtyProperties();
        }

        protected override void PersistUpdatedItem(IContent entity)
        {
            var content = (Content) entity;
            var publishedState = content.PublishedState;
            var publishedStateChanged = publishedState == PublishedState.Publishing || publishedState == PublishedState.Unpublishing;

            //check if we need to make any database changes at all
            if (entity.RequiresSaving(publishedState) == false)
            {
                entity.ResetDirtyProperties();
                return;
            }

            //check if we need to create a new version
            var requiresNewVersion = entity.RequiresNewVersion(publishedState);
            if (requiresNewVersion)
            {
                //Updates Modified date and Version Guid
                content.UpdatingEntity();
            }
            else
            {
                if (entity.IsPropertyDirty("UpdateDate") == false || entity.UpdateDate == default(DateTime))
                    entity.UpdateDate = DateTime.Now;
            }

            //Ensure unique name on the same level
            entity.Name = EnsureUniqueNodeName(entity.ParentId, entity.Name, entity.Id);

            //Ensure that strings don't contain characters that are invalid in XML
            entity.SanitizeEntityPropertiesForXmlStorage();

            //Look up parent to get and set the correct Path and update SortOrder if ParentId has changed
            if (entity.IsPropertyDirty("ParentId"))
            {
                var parent = Database.First<NodeDto>("WHERE id = @ParentId", new { /*ParentId =*/ entity.ParentId });
                entity.Path = string.Concat(parent.Path, ",", entity.Id);
                entity.Level = parent.Level + 1;
                entity.SortOrder = NextChildSortOrder(entity.ParentId);

                //Question: If we move a node, should we update permissions to inherit from the new parent if the parent has permissions assigned?
                // if we do that, then we'd need to propogate permissions all the way downward which might not be ideal for many people.
                // Gonna just leave it as is for now, and not re-propogate permissions.
            }

            var factory = new ContentFactory(NodeObjectTypeId, entity.Id);
            //Look up Content entry to get Primary for updating the DTO
            var contentDto = Database.SingleOrDefault<ContentDto>("WHERE nodeId = @Id", new { /*Id =*/ entity.Id });
            factory.SetPrimaryKey(contentDto.PrimaryKey);
            var dto = factory.BuildDto(entity);

            //Updates the (base) node data - umbracoNode
            var nodeDto = dto.ContentVersionDto.ContentDto.NodeDto;
            nodeDto.ValidatePathWithException();
            var unused = Database.Update(nodeDto);

            //Only update this DTO if the contentType has actually changed
            if (contentDto.ContentTypeId != entity.ContentTypeId)
            {
                //Create the Content specific data - cmsContent
                var newContentDto = dto.ContentVersionDto.ContentDto;
                Database.Update(newContentDto);
            }

            //If Published state has changed then previous versions should have their publish state reset.
            //If state has been changed to unpublished the previous versions publish state should also be reset.
            //if (((ICanBeDirty)entity).IsPropertyDirty("Published") && (entity.Published || publishedState == PublishedState.Unpublished))
            if (entity.RequiresClearPublishedFlag(publishedState, requiresNewVersion))
                ClearPublishedFlag(entity);

            //Look up (newest) entries by id in cmsDocument table to set newest = false
            ClearNewestFlag(entity);

            var contentVersionDto = dto.ContentVersionDto;
            if (requiresNewVersion)
            {
                //Create a new version - cmsContentVersion
                //Assumes a new Version guid and Version date (modified date) has been set
                Database.Insert(contentVersionDto);
                //Create the Document specific data for this version - cmsDocument
                //Assumes a new Version guid has been generated
                Database.Insert(dto);
            }
            else
            {
                //In order to update the ContentVersion we need to retrieve its primary key id
                var contentVerDto = Database.SingleOrDefault<ContentVersionDto>("WHERE VersionId = @Version", new { /*Version =*/ entity.Version });
                contentVersionDto.Id = contentVerDto.Id;

                Database.Update(contentVersionDto);
                Database.Update(dto);
            }

            //Create the PropertyData for this version - cmsPropertyData
            var propertyFactory = new PropertyFactory(entity.ContentType.CompositionPropertyTypes.ToArray(), entity.Version, entity.Id);
            var propertyDataDtos = propertyFactory.BuildDto(entity.Properties);
            var keyDictionary = new Dictionary<int, int>();

            //Add Properties
            foreach (var propertyDataDto in propertyDataDtos)
            {
                if (requiresNewVersion == false && propertyDataDto.Id > 0)
                {
                    Database.Update(propertyDataDto);
                }
                else
                {
                    int primaryKey = Convert.ToInt32(Database.Insert(propertyDataDto));
                    keyDictionary.Add(propertyDataDto.PropertyTypeId, primaryKey);
                }
            }

            //Update Properties with its newly set Id
            if (keyDictionary.Any())
            {
                foreach (var property in entity.Properties)
                {
                    if (keyDictionary.ContainsKey(property.PropertyTypeId) == false) continue;

                    property.Id = keyDictionary[property.PropertyTypeId];
                }
            }

            // tags:
            if (HasTagProperty(entity))
            {
                // if path-published, update tags, else clear tags
                switch (content.PublishedState)
                {
                    case PublishedState.Publishing:
                        // explicitely publishing, must update tags
                        UpdateEntityTags(entity, _tagRepository);
                        break;
                    case PublishedState.Unpublishing:
                        // explicitely unpublishing, must clear tags
                        ClearEntityTags(entity, _tagRepository);
                        break;
                    case PublishedState.Saving:
                        // saving, nothing to do
                        break;
                    case PublishedState.Published:
                    case PublishedState.Unpublished:
                        // no change, depends on path-published
                        // that should take care of trashing and un-trashing
                        if (IsPathPublished(entity)) // slightly expensive ;-(
                            UpdateEntityTags(entity, _tagRepository);
                        else
                            ClearEntityTags(entity, _tagRepository);
                        break;
                }
            }

            // published => update published version infos,
            // else if unpublished then clear published version infos
            // else leave unchanged
            if (entity.Published)
            {
                dto.DocumentPublishedReadOnlyDto = new DocumentPublishedReadOnlyDto
                {
                    VersionId = dto.VersionId,
                    VersionDate = dto.UpdateDate,
                    Newest = true,
                    NodeId = dto.NodeId,
                    Published = true
                };
                content.PublishedVersionGuid = dto.VersionId;
                content.PublishedDate = dto.UpdateDate;
            }
            else if (publishedStateChanged)
            {
                dto.DocumentPublishedReadOnlyDto = new DocumentPublishedReadOnlyDto
                {
                    VersionId = default(Guid),
                    VersionDate = default (DateTime),
                    Newest = false,
                    NodeId = dto.NodeId,
                    Published = false
                };
                content.PublishedVersionGuid = default(Guid);
                content.PublishedDate = dto.UpdateDate;
            }

            OnUowRefreshedEntity(new UnitOfWorkEntityEventArgs(UnitOfWork, entity));

            entity.ResetDirtyProperties();
        }

        private int NextChildSortOrder(int parentId)
        {
            var maxSortOrder =
                Database.ExecuteScalar<int>(
                    "SELECT coalesce(max(sortOrder),0) FROM umbracoNode WHERE parentid = @ParentId AND nodeObjectType = @NodeObjectType",
                    new { ParentId = parentId, NodeObjectType = NodeObjectTypeId });
            return maxSortOrder + 1;
        }

        #endregion

        #region Implementation of IContentRepository

        public IEnumerable<IContent> GetByPublishedVersion(IQuery<IContent> query)
        {
            // we WANT to return contents in top-down order, ie parents should come before children
            // ideal would be pure xml "document order" - which we cannot achieve at database level

            var sqlClause = GetBaseQuery(QueryType.Many);
            var translator = new SqlTranslator<IContent>(sqlClause, query);
            var sql = translator.Translate()
                                .Where<DocumentDto>(x => x.Published)
                                .OrderBy<NodeDto>(x => x.Level)
                                .OrderBy<NodeDto>(x => x.SortOrder);

            return MapQueryDtos(Database.Fetch<DocumentDto>(sql), true, many: true);
        }

        public int CountPublished(string contentTypeAlias = null)
        {
            var sql = Sql();
            if (contentTypeAlias.IsNullOrWhiteSpace())
            {
                sql.SelectCount()
                    .From<NodeDto>()
                    .InnerJoin<DocumentDto>()
                    .On<NodeDto, DocumentDto>(left => left.NodeId, right => right.NodeId)
                    .Where<NodeDto>(x => x.NodeObjectType == NodeObjectTypeId && x.Trashed == false)
                    .Where<DocumentDto>(x => x.Published);
            }
            else
            {
                sql.SelectCount()
                    .From<NodeDto>()
                    .InnerJoin<ContentDto>()
                    .On<NodeDto, ContentDto>(left => left.NodeId, right => right.NodeId)
                    .InnerJoin<DocumentDto>()
                    .On<NodeDto, DocumentDto>(left => left.NodeId, right => right.NodeId)
                    .InnerJoin<ContentTypeDto>()
                    .On<ContentTypeDto, ContentDto>(left => left.NodeId, right => right.ContentTypeId)
                    .Where<NodeDto>(x => x.NodeObjectType == NodeObjectTypeId && x.Trashed == false)
                    .Where<ContentTypeDto>(x => x.Alias == contentTypeAlias)
                    .Where<DocumentDto>(x => x.Published);
            }

            return Database.ExecuteScalar<int>(sql);
        }

        public void ReplaceContentPermissions(EntityPermissionSet permissionSet)
        {
            PermissionRepository.ReplaceEntityPermissions(permissionSet);
        }

        public void ClearPublishedFlag(IContent content)
        {
            var sql = "UPDATE cmsDocument SET published=0 WHERE nodeId=@id AND published=1";
            Database.Execute(sql, new { id = content.Id });
        }

        public void ClearNewestFlag(IContent content)
        {
            var sql = "UPDATE cmsDocument SET newest=0 WHERE nodeId=@id AND newest=1";
            Database.Execute(sql, new { id = content.Id });
        }

        /// <summary>
        /// Assigns a single permission to the current content item for the specified user ids
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="permission"></param>
        /// <param name="userIds"></param>
        public void AssignEntityPermission(IContent entity, char permission, IEnumerable<int> userIds)
        {
            PermissionRepository.AssignEntityPermission(entity, permission, userIds);
        }

        public IEnumerable<EntityPermission> GetPermissionsForEntity(int entityId)
        {
            return PermissionRepository.GetPermissionsForEntity(entityId);
        }

        /// <summary>
        /// Gets paged content results
        /// </summary>
        /// <param name="query">Query to excute</param>
        /// <param name="pageIndex">Page number</param>
        /// <param name="pageSize">Page size</param>
        /// <param name="totalRecords">Total records query would return without paging</param>
        /// <param name="orderBy">Field to order by</param>
        /// <param name="orderDirection">Direction to order by</param>
        /// <param name="orderBySystemField">Flag to indicate when ordering by system field</param>
        /// <param name="filter"></param>
        /// <returns>An Enumerable list of <see cref="IContent"/> objects</returns>
        public IEnumerable<IContent> GetPagedResultsByQuery(IQuery<IContent> query, long pageIndex, int pageSize, out long totalRecords,
            string orderBy, Direction orderDirection, bool orderBySystemField, IQuery<IContent> filter = null, bool newest = true)
        {
            var filterSql = Sql();
            if (newest)
                filterSql.Append("AND (cmsDocument.newest = 1)");

            if (filter != null)
            {
                foreach (var filterClause in filter.GetWhereClauses())
                    filterSql.Append($"AND ({filterClause.Item1})", filterClause.Item2);
            }

            return GetPagedResultsByQuery<DocumentDto>(query, pageIndex, pageSize, out totalRecords,
                x => MapQueryDtos(x, many: true),
                orderBy, orderDirection, orderBySystemField, "cmsDocument",
                filterSql);
        }

        public bool IsPathPublished(IContent content)
        {
            // fail fast
            if (content.Path.StartsWith("-1,-20,"))
                return false;
            // succeed fast
            if (content.ParentId == -1)
                return content.HasPublishedVersion;

            var syntaxUmbracoNode = SqlSyntax.GetQuotedTableName("umbracoNode");
            var syntaxPath = SqlSyntax.GetQuotedColumnName("path");
            var syntaxConcat = SqlSyntax.GetConcat(syntaxUmbracoNode + "." + syntaxPath, "',%'");

            var sql = string.Format(@"SELECT COUNT({0}.{1})
FROM {0}
JOIN {2} ON ({0}.{1}={2}.{3} AND {2}.{4}=@published)
WHERE (@path LIKE {5})",
                syntaxUmbracoNode,
                SqlSyntax.GetQuotedColumnName("id"),
                SqlSyntax.GetQuotedTableName("cmsDocument"),
                SqlSyntax.GetQuotedColumnName("nodeId"),
                SqlSyntax.GetQuotedColumnName("published"),
                syntaxConcat);

            var count = Database.ExecuteScalar<int>(sql, new { @published=true, @path=content.Path });
            count += 1; // because content does not count
            return count == content.Level;
        }

        #endregion

        #region IRecycleBinRepository members

        protected override int RecycleBinId => Constants.System.RecycleBinContent;

        #endregion

        protected override string GetDatabaseFieldNameForOrderBy(string orderBy)
        {
            // NOTE see sortby.prevalues.controller.js for possible values
            // that need to be handled here or in VersionableRepositoryBase

            //Some custom ones
            switch (orderBy.ToUpperInvariant())
            {
                case "UPDATER":
                    //TODO: This isn't going to work very nicely because it's going to order by ID, not by letter
                    return GetDatabaseFieldNameForOrderBy("cmsDocument", "documentUser");
                case "PUBLISHED":
                    return GetDatabaseFieldNameForOrderBy("cmsDocument", "published");
                case "CONTENTTYPEALIAS":
                    throw new NotSupportedException("Don't know how to support ContentTypeAlias.");
            }

            return base.GetDatabaseFieldNameForOrderBy(orderBy);
        }

        // "many" corresponds to 7.6 "includeAllVersions"
        // fixme - we are not implementing the double-query thing for pagination from 7.6?
        //
        private IEnumerable<IContent> MapQueryDtos(List<DocumentDto> dtos, bool withCache = false, bool many = false)
        {
            var content = new IContent[dtos.Count];
            var temps = new List<TempContent>();
            var contentTypes = new Dictionary<int, IContentType>();
            var templateIds = new List<int>();

            // in case of data corruption we may have more than 1 "newest" - cleanup
            var ix = new Dictionary<int, DocumentDto>();
            foreach (var dto in dtos)
            {
                if (ix.TryGetValue(dto.NodeId, out DocumentDto ixDto) == false || ixDto.UpdateDate < dto.UpdateDate)
                    ix[dto.NodeId] = dto;
            }
            dtos = ix.Values.ToList();

            // populate published data
            if (many)
            {
                var roDtos = Database.FetchByGroups<DocumentPublishedReadOnlyDto, int>(dtos.Select(x => x.NodeId), 2000, batch
                        => Sql()
                            .Select<DocumentPublishedReadOnlyDto>()
                            .From<DocumentDto>()
                            .WhereIn<DocumentDto>(x => x.NodeId, batch)
                            .Where<DocumentDto>(x => x.Published));

                // in case of data corruption we may have more than 1 "published" - cleanup
                var publishedDtoIndex = new Dictionary<int, DocumentPublishedReadOnlyDto>();
                foreach (var roDto in roDtos)
                {
                    if (publishedDtoIndex.TryGetValue(roDto.NodeId, out DocumentPublishedReadOnlyDto ixDto) == false || ixDto.VersionDate < roDto.VersionDate)
                        publishedDtoIndex[roDto.NodeId] = roDto;
                }

                foreach (var dto in dtos)
                {
                    if (publishedDtoIndex.TryGetValue(dto.NodeId, out DocumentPublishedReadOnlyDto d) == false)
                        d = new DocumentPublishedReadOnlyDto();
                    dto.DocumentPublishedReadOnlyDto = d;
                }
            }

            for (var i = 0; i < dtos.Count; i++)
            {
                var dto = dtos[i];

                if (withCache)
                {
                    // if the cache contains the (proper version of the) item, use it
                    var cached = IsolatedCache.GetCacheItem<IContent>(GetCacheIdKey<IContent>(dto.NodeId));
                    // fixme - wtf? only published?
                    if (cached != null && cached.Published)
                    //if (cached != null && cached.Version == dto.ContentVersionDto.VersionId)
                    {
                        content[i] = cached;
                        continue;
                    }
                }

                // else, need to fetch from the database

                // get the content type - the repository is full cache *but* still deep-clones
                // whatever comes out of it, so use our own local index here to avoid this
                if (contentTypes.TryGetValue(dto.ContentVersionDto.ContentDto.ContentTypeId, out IContentType contentType) == false)
                    contentTypes[dto.ContentVersionDto.ContentDto.ContentTypeId] = contentType = _contentTypeRepository.Get(dto.ContentVersionDto.ContentDto.ContentTypeId);

                var c = content[i] = ContentFactory.BuildEntity(dto, contentType, dto.DocumentPublishedReadOnlyDto);

                // need template
                if (dto.TemplateId.HasValue && dto.TemplateId.Value > 0)
                    templateIds.Add(dto.TemplateId.Value);

                // need properties
                temps.Add(new TempContent(
                    dto.NodeId,
                    dto.VersionId,
                    dto.ContentVersionDto.VersionDate,
                    dto.ContentVersionDto.ContentDto.NodeDto.CreateDate,
                    contentType,
                    c
                ) { TemplateId = dto.TemplateId });
            }

            // load all required templates in 1 query and index
            var templates = _templateRepository.GetAll(templateIds.ToArray())
                .ToDictionary(x => x.Id, x => x);

            // load all properties for all documents from database in 1 query
            var propertyData = GetPropertyCollection(temps);

            // assign
            foreach (var temp in temps)
            {
                // complete the item
                ITemplate template = null;
                if (temp.TemplateId.HasValue)
                    templates.TryGetValue(temp.TemplateId.Value, out template); // else null
                ((Content) temp.Content).Template = template;
                temp.Content.Properties = propertyData[temp.Version];

                //on initial construction we don't want to have dirty properties tracked
                // http://issues.umbraco.org/issue/U4-1946
                ((Entity) temp.Content).ResetDirtyProperties(false);
            }

            return content;
        }

        /// <summary>
        /// Private method to create a content object from a DocumentDto, which is used by Get and GetByVersion.
        /// </summary>
        /// <param name="dto"></param>
        /// <param name="versionId"></param>
        /// <returns></returns>
        private IContent CreateContentFromDto(DocumentDto dto, Guid versionId)
        {
            var contentType = _contentTypeRepository.Get(dto.ContentVersionDto.ContentDto.ContentTypeId);

            var factory = new ContentFactory(contentType, NodeObjectTypeId, dto.NodeId);
            var content = factory.BuildEntity(dto);

            //Check if template id is set on DocumentDto, and get ITemplate if it is.
            if (dto.TemplateId.HasValue && dto.TemplateId.Value > 0)
            {
                content.Template = _templateRepository.Get(dto.TemplateId.Value);
            }

            var docDef = new TempContent(dto.NodeId, versionId, content.UpdateDate, content.CreateDate, contentType);

            var properties = GetPropertyCollection(new List<TempContent> { docDef });

            content.Properties = properties[versionId];

            //on initial construction we don't want to have dirty properties tracked
            // http://issues.umbraco.org/issue/U4-1946
            ((Entity)content).ResetDirtyProperties(false);
            return content;
        }

        private string EnsureUniqueNodeName(int parentId, string nodeName, int id = 0)
        {
            if (EnsureUniqueNaming == false)
                return nodeName;

            var sql = Sql()
                .SelectAll()
                .From<NodeDto>()
                .Where<NodeDto>(x => x.NodeObjectType == NodeObjectTypeId && x.ParentId == parentId && x.Text.StartsWith(nodeName));

            int uniqueNumber = 1;
            var currentName = nodeName;

            var dtos = Database.Fetch<NodeDto>(sql);
            if (dtos.Any())
            {
                var results = dtos.OrderBy(x => x.Text, new SimilarNodeNameComparer());
                foreach (var dto in results)
                {
                    if (id != 0 && id == dto.NodeId) continue;

                    if (dto.Text.ToLowerInvariant().Equals(currentName.ToLowerInvariant()))
                    {
                        currentName = nodeName + $" ({uniqueNumber})";
                        uniqueNumber++;
                    }
                }
            }

            return currentName;
        }
    }
}
