﻿using System;
using System.Data.SqlServerCe;
using System.Linq;
using NUnit.Framework;
using Umbraco.Core.Cache;
using Umbraco.Core.Models;
using Umbraco.Core.Persistence.Repositories;
using Umbraco.Core.Persistence.UnitOfWork;
using Umbraco.Tests.TestHelpers;
using Umbraco.Tests.Testing;

namespace Umbraco.Tests.Persistence.Repositories
{
    [TestFixture]
    [UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
    public class ServerRegistrationRepositoryTest : TestWithDatabaseBase
    {
        private CacheHelper _cacheHelper;

        public override void SetUp()
        {
            base.SetUp();

            _cacheHelper = new CacheHelper();
            CreateTestData();
        }

        private ServerRegistrationRepository CreateRepository(IScopeUnitOfWork unitOfWork)
        {
            return new ServerRegistrationRepository(unitOfWork, Logger);
        }

        [Test]
        public void Cannot_Add_Duplicate_Server_Identities()
        {
            // Arrange
            var provider = TestObjects.GetScopeUnitOfWorkProvider(Logger);
            using (var unitOfWork = provider.CreateUnitOfWork())
            {
                var repository = CreateRepository(unitOfWork);

                var server = new ServerRegistration("http://shazwazza.com", "COMPUTER1", DateTime.Now);
                repository.AddOrUpdate(server);

                Assert.Throws<SqlCeException>(unitOfWork.Flush);
            }

        }

        [Test]
        public void Cannot_Update_To_Duplicate_Server_Identities()
        {
            // Arrange
            var provider = TestObjects.GetScopeUnitOfWorkProvider(Logger);
            using (var unitOfWork = provider.CreateUnitOfWork())
            {
                var repository = CreateRepository(unitOfWork);

                var server = repository.Get(1);
                server.ServerIdentity = "COMPUTER2";
                repository.AddOrUpdate(server);
                Assert.Throws<SqlCeException>(unitOfWork.Flush);
            }

        }

        [Test]
        public void Can_Instantiate_Repository()
        {
            // Arrange
            var provider = TestObjects.GetScopeUnitOfWorkProvider(Logger);
            using (var unitOfWork = provider.CreateUnitOfWork())
            {
                var repository = CreateRepository(unitOfWork);

                // Assert
                Assert.That(repository, Is.Not.Null);
            }
        }

        [Test]
        public void Can_Perform_Get_On_Repository()
        {
            // Arrange
            var provider = TestObjects.GetScopeUnitOfWorkProvider(Logger);
            using (var unitOfWork = provider.CreateUnitOfWork())
            {
                var repository = CreateRepository(unitOfWork);

                // Act
                var server = repository.Get(1);

                // Assert
                Assert.That(server, Is.Not.Null);
                Assert.That(server.HasIdentity, Is.True);
                Assert.That(server.ServerAddress, Is.EqualTo("http://localhost"));
            }


        }

        [Test]
        public void Can_Perform_GetAll_On_Repository()
        {
            // Arrange
            var provider = TestObjects.GetScopeUnitOfWorkProvider(Logger);
            using (var unitOfWork = provider.CreateUnitOfWork())
            {
                var repository = CreateRepository(unitOfWork);

                // Act
                var servers = repository.GetAll();

                // Assert
                Assert.That(servers.Count(), Is.EqualTo(3));
            }

        }

        // queries are not supported due to in-memory caching

        //[Test]
        //public void Can_Perform_GetByQuery_On_Repository()
        //{
        //    // Arrange
        //    var provider = TestObjects.GetScopeUnitOfWorkProvider(Logger);
        //    using (var unitOfWork = provider.GetUnitOfWork())
        //    using (var repository = CreateRepository(unitOfWork))
        //    {
        //        // Act
        //        var query = Query<IServerRegistration>.Builder.Where(x => x.ServerIdentity.ToUpper() == "COMPUTER3");
        //        var result = repository.GetByQuery(query);

        //        // Assert
        //        Assert.AreEqual(1, result.Count());
        //    }
        //}

        //[Test]
        //public void Can_Perform_Count_On_Repository()
        //{
        //    // Arrange
        //    var provider = TestObjects.GetScopeUnitOfWorkProvider(Logger);
        //    using (var unitOfWork = provider.GetUnitOfWork())
        //    using (var repository = CreateRepository(unitOfWork))
        //    {
        //        // Act
        //        var query = Query<IServerRegistration>.Builder.Where(x => x.ServerAddress.StartsWith("http://"));
        //        int count = repository.Count(query);

        //        // Assert
        //        Assert.That(count, Is.EqualTo(2));
        //    }
        //}

        [Test]
        public void Can_Perform_Add_On_Repository()
        {
            // Arrange
            var provider = TestObjects.GetScopeUnitOfWorkProvider(Logger);
            using (var unitOfWork = provider.CreateUnitOfWork())
            {
                var repository = CreateRepository(unitOfWork);

                // Act
                var server = new ServerRegistration("http://shazwazza.com", "COMPUTER4", DateTime.Now);
                repository.AddOrUpdate(server);
                unitOfWork.Flush();

                // Assert
                Assert.That(server.HasIdentity, Is.True);
                Assert.That(server.Id, Is.EqualTo(4));//With 3 existing entries the Id should be 4
            }
        }

        [Test]
        public void Can_Perform_Update_On_Repository()
        {
            // Arrange
            var provider = TestObjects.GetScopeUnitOfWorkProvider(Logger);
            using (var unitOfWork = provider.CreateUnitOfWork())
            {
                var repository = CreateRepository(unitOfWork);

                // Act
                var server = repository.Get(2);
                server.ServerAddress = "https://umbraco.com";
                server.IsActive = true;

                repository.AddOrUpdate(server);
                unitOfWork.Flush();

                var serverUpdated = repository.Get(2);

                // Assert
                Assert.That(serverUpdated, Is.Not.Null);
                Assert.That(serverUpdated.ServerAddress, Is.EqualTo("https://umbraco.com"));
                Assert.That(serverUpdated.IsActive, Is.EqualTo(true));
            }
        }

        [Test]
        public void Can_Perform_Delete_On_Repository()
        {
            // Arrange
            var provider = TestObjects.GetScopeUnitOfWorkProvider(Logger);
            using (var unitOfWork = provider.CreateUnitOfWork())
            {
                var repository = CreateRepository(unitOfWork);

                // Act
                var server = repository.Get(3);
                Assert.IsNotNull(server);
                repository.Delete(server);
                unitOfWork.Flush();

                var exists = repository.Exists(3);

                // Assert
                Assert.That(exists, Is.False);
            }
        }

        [Test]
        public void Can_Perform_Exists_On_Repository()
        {
            // Arrange
            var provider = TestObjects.GetScopeUnitOfWorkProvider(Logger);
            using (var unitOfWork = provider.CreateUnitOfWork())
            {
                var repository = CreateRepository(unitOfWork);

                // Act
                var exists = repository.Exists(3);
                var doesntExist = repository.Exists(10);

                // Assert
                Assert.That(exists, Is.True);
                Assert.That(doesntExist, Is.False);
            }
        }

        [TearDown]
        public override void TearDown()
        {
            base.TearDown();
        }

        public void CreateTestData()
        {
            var provider = TestObjects.GetScopeUnitOfWorkProvider(Logger);
            using (var unitOfWork = provider.CreateUnitOfWork())
            {
                var repository = CreateRepository(unitOfWork);

                repository.AddOrUpdate(new ServerRegistration("http://localhost", "COMPUTER1", DateTime.Now) { IsActive = true });
                repository.AddOrUpdate(new ServerRegistration("http://www.mydomain.com", "COMPUTER2", DateTime.Now));
                repository.AddOrUpdate(new ServerRegistration("https://www.another.domain.com", "Computer3", DateTime.Now));
                unitOfWork.Complete();
            }

        }
    }
}
