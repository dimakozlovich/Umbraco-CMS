﻿using System;
using System.Collections.Generic;
using Umbraco.Core.Models.PublishedContent;
using Umbraco.Web.PublishedCache.NuCache.DataSource;

namespace Umbraco.Web.PublishedCache.NuCache
{
    // represents a content "node" ie a pair of draft + published versions
    // internal, never exposed, to be accessed from ContentStore (only!)
    internal class ContentNode
    {
        // special ctor with no content data - for members
        public ContentNode(int id, Guid uid, PublishedContentType contentType,
            int level, string path, int sortOrder,
            int parentContentId,
            DateTime createDate, int creatorId)
        {
            Id = id;
            Uid = uid;
            ContentType = contentType;
            Level = level;
            Path = path;
            SortOrder = sortOrder;
            ParentContentId = parentContentId;
            CreateDate = createDate;
            CreatorId = creatorId;

            ChildContentIds = new List<int>();
        }

        public ContentNode(int id, Guid uid, PublishedContentType contentType,
            int level, string path, int sortOrder,
            int parentContentId,
            DateTime createDate, int creatorId,
            ContentData draftData, ContentData publishedData,
            IFacadeAccessor facadeAccessor)
            : this(id, uid, level, path, sortOrder, parentContentId, createDate, creatorId)
        {
            SetContentTypeAndData(contentType, draftData, publishedData, facadeAccessor);
        }

        // 2-phases ctor, phase 1
        public ContentNode(int id, Guid uid,
            int level, string path, int sortOrder,
            int parentContentId,
            DateTime createDate, int creatorId)
        {
            Id = id;
            Uid = uid;
            Level = level;
            Path = path;
            SortOrder = sortOrder;
            ParentContentId = parentContentId;
            CreateDate = createDate;
            CreatorId = creatorId;

            ChildContentIds = new List<int>();
        }

        // two-phase ctor, phase 2
        public void SetContentTypeAndData(PublishedContentType contentType, ContentData draftData, ContentData publishedData, IFacadeAccessor facadeAccessor)
        {
            ContentType = contentType;

            if (draftData == null && publishedData == null)
                throw new ArgumentException("Both draftData and publishedData cannot be null at the same time.");

            if (draftData != null)
                Draft = new PublishedContent(this, draftData, facadeAccessor).CreateModel();
            if (publishedData != null)
                Published = new PublishedContent(this, publishedData, facadeAccessor).CreateModel();
        }

        // clone parent
        private ContentNode(ContentNode origin)
        {
            // everything is the same, except for the child items
            // list which is a clone of the original list

            Id = origin.Id;
            Uid = origin.Uid;
            ContentType = origin.ContentType;
            Level = origin.Level;
            Path = origin.Path;
            SortOrder = origin.SortOrder;
            ParentContentId = origin.ParentContentId;
            CreateDate = origin.CreateDate;
            CreatorId = origin.CreatorId;

            var originDraft = origin.Draft == null ? null : PublishedContent.UnwrapIPublishedContent(origin.Draft);
            var originPublished = origin.Published == null ? null : PublishedContent.UnwrapIPublishedContent(origin.Published);

            Draft = originDraft == null ? null : new PublishedContent(this, originDraft).CreateModel();
            Published = originPublished == null ? null : new PublishedContent(this, originPublished).CreateModel();

            ChildContentIds = new List<int>(origin.ChildContentIds); // needs to be *another* list
        }

        // clone with new content type
        public ContentNode(ContentNode origin, PublishedContentType contentType, IFacadeAccessor facadeAccessor)
        {
            Id = origin.Id;
            Uid = origin.Uid;
            ContentType = contentType; // change!
            Level = origin.Level;
            Path = origin.Path;
            SortOrder = origin.SortOrder;
            ParentContentId = origin.ParentContentId;
            CreateDate = origin.CreateDate;
            CreatorId = origin.CreatorId;

            var originDraft = origin.Draft == null ? null : PublishedContent.UnwrapIPublishedContent(origin.Draft);
            var originPublished = origin.Published == null ? null : PublishedContent.UnwrapIPublishedContent(origin.Published);

            Draft = originDraft == null ? null : new PublishedContent(this, originDraft._contentData, facadeAccessor).CreateModel();
            Published = originPublished == null ? null : new PublishedContent(this, originPublished._contentData, facadeAccessor).CreateModel();

            ChildContentIds = origin.ChildContentIds; // can be the *same* list FIXME oh really?
        }

        // everything that is common to both draft and published versions
        // keep this as small as possible
        public readonly int Id;
        public readonly Guid Uid;
        public PublishedContentType ContentType;
        public readonly int Level;
        public readonly string Path;
        public readonly int SortOrder;
        public readonly int ParentContentId;
        public List<int> ChildContentIds;
        public readonly DateTime CreateDate;
        public readonly int CreatorId;

        // draft and published version (either can be null, but not both)
        public IPublishedContent Draft;
        public IPublishedContent Published;

        public ContentNode CloneParent(IFacadeAccessor facadeAccessor)
        {
            return new ContentNode(this);
        }

        public ContentNodeKit ToKit()
        {
            return new ContentNodeKit
            {
                Node = this,
                ContentTypeId = ContentType.Id,

                DraftData = ((PublishedContent) Draft)?._contentData,
                PublishedData = ((PublishedContent) Published)?._contentData
            };
        }
    }
}
