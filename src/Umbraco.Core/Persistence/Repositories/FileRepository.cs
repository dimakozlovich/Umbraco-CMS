﻿using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Umbraco.Core.IO;
using Umbraco.Core.Models;
using Umbraco.Core.Models.EntityBase;
using Umbraco.Core.Persistence.UnitOfWork;

namespace Umbraco.Core.Persistence.Repositories
{
    internal abstract class FileRepository<TId, TEntity> : DisposableObject, IUnitOfWorkRepository, IRepository<TId, TEntity>
        where TEntity : IFile
    {
        protected FileRepository(IUnitOfWork work, IFileSystem fileSystem)
        {
            UnitOfWork = work;
            FileSystem = fileSystem;
        }

        /// <summary>
        /// Returns the Unit of Work added to the repository
        /// </summary>
        protected IUnitOfWork UnitOfWork { get; }

        protected IFileSystem FileSystem { get; }

        public virtual void AddFolder(string folderPath)
        {
            UnitOfWork.RegisterCreated(new Folder(folderPath), this);
        }

        public virtual void DeleteFolder(string folderPath)
        {
            UnitOfWork.RegisterDeleted(new Folder(folderPath), this);
        }

        #region Implementation of IRepository<TId,TEntity>

        public virtual void AddOrUpdate(TEntity entity)
        {
            if (FileSystem.FileExists(entity.OriginalPath) == false)
            {
                UnitOfWork.RegisterCreated(entity, this);
            }
            else
            {
                UnitOfWork.RegisterUpdated(entity, this);
            }
        }

        public virtual void Delete(TEntity entity)
        {
            if (UnitOfWork != null)
            {
                UnitOfWork.RegisterDeleted(entity, this);
            }
        }

        public abstract TEntity Get(TId id);

        public abstract IEnumerable<TEntity> GetAll(params TId[] ids);

        public virtual bool Exists(TId id)
        {
            return FileSystem.FileExists(id.ToString());
        }

        #endregion

        #region Implementation of IUnitOfWorkRepository

        public void PersistNewItem(IEntity entity)
        {
            //special case for folder
            var folder = entity as Folder;
            if (folder != null)
            {
                PersistNewFolder(folder);
            }
            else
            {
                PersistNewItem((TEntity)entity);
            }
        }

        public void PersistUpdatedItem(IEntity entity)
        {
            PersistUpdatedItem((TEntity)entity);
        }

        public void PersistDeletedItem(IEntity entity)
        {
            //special case for folder
            var folder = entity as Folder;
            if (folder != null)
            {
                PersistDeletedFolder(folder);
            }
            else
            {
                PersistDeletedItem((TEntity)entity);
            }
        }

        #endregion

        internal virtual void PersistNewFolder(Folder entity)
        {
            FileSystem.CreateFolder(entity.Path);
        }

        internal virtual void PersistDeletedFolder(Folder entity)
        {
            FileSystem.DeleteDirectory(entity.Path);
        }

        #region Abstract IUnitOfWorkRepository Methods

        protected virtual void PersistNewItem(TEntity entity)
        {
            using (var stream = GetContentStream(entity.Content))
            {
                FileSystem.AddFile(entity.Path, stream, true);
                entity.CreateDate = FileSystem.GetCreated(entity.Path).UtcDateTime;
                entity.UpdateDate = FileSystem.GetLastModified(entity.Path).UtcDateTime;
                //the id can be the hash
                entity.Id = entity.Path.GetHashCode();
                entity.Key = entity.Path.EncodeAsGuid();
                entity.VirtualPath = FileSystem.GetUrl(entity.Path);
            }
        }

        protected virtual void PersistUpdatedItem(TEntity entity)
        {
            using (var stream = GetContentStream(entity.Content))
            {
                FileSystem.AddFile(entity.Path, stream, true);
                entity.CreateDate = FileSystem.GetCreated(entity.Path).UtcDateTime;
                entity.UpdateDate = FileSystem.GetLastModified(entity.Path).UtcDateTime;
                //the id can be the hash
                entity.Id = entity.Path.GetHashCode();
                entity.Key = entity.Path.EncodeAsGuid();
                entity.VirtualPath = FileSystem.GetUrl(entity.Path);
            }

            //now that the file has been written, we need to check if the path had been changed
            if (entity.Path.InvariantEquals(entity.OriginalPath) == false)
            {
                //delete the original file
                FileSystem.DeleteFile(entity.OriginalPath);
                //reset the original path on the file
                entity.ResetOriginalPath();
            }
        }

        protected virtual void PersistDeletedItem(TEntity entity)
        {
            if (FileSystem.FileExists(entity.Path))
            {
                FileSystem.DeleteFile(entity.Path);
            }
        }

        #endregion

        /// <summary>
        /// Gets a stream that is used to write to the file
        /// </summary>
        /// <param name="content"></param>
        /// <returns></returns>
        protected virtual Stream GetContentStream(string content)
        {
            return new MemoryStream(Encoding.UTF8.GetBytes(content));
        }

        /// <summary>
        /// Returns all files in the file system
        /// </summary>
        /// <param name="path"></param>
        /// <param name="filter"></param>
        /// <returns>
        /// Returns a list of all files with their paths. For example:
        ///
        /// \hello.txt
        /// \folder1\test.txt
        /// \folder1\blah.csv
        /// \folder1\folder2\blahhhhh.svg
        /// </returns>
        protected IEnumerable<string> FindAllFiles(string path, string filter)
        {
            var list = new List<string>();
            list.AddRange(FileSystem.GetFiles(path, filter));

            var directories = FileSystem.GetDirectories(path);
            foreach (var directory in directories)
            {
                list.AddRange(FindAllFiles(directory, filter));
            }

            return list;
        }

        protected string GetFileContent(string filename)
        {
            if (FileSystem.FileExists(filename) == false)
                return null;

            try
            {
                using (var stream = FileSystem.OpenFile(filename))
                using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                {
                    return reader.ReadToEnd();
                }
            }
            catch
            {
                return null; // deal with race conds
            }
        }

        public long GetFileSize(string filename)
        {
            if (FileSystem.FileExists(filename) == false)
                return -1;

            try
            {
                return FileSystem.GetSize(filename);
            }
            catch
            {
                return -1; // deal with race conds
            }
        }

        /// <summary>
        /// Dispose any disposable properties
        /// </summary>
        /// <remarks>
        /// Dispose the unit of work
        /// </remarks>
        protected override void DisposeResources()
        {
            // fixme - wtf in v8?
            UnitOfWork.DisposeIfDisposable();
        }
    }
}
