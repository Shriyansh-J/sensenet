﻿using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SenseNet.ContentRepository.Storage;
using SenseNet.ContentRepository.Storage.Data;
using SenseNet.ContentRepository.Storage.Security;

namespace SenseNet.Tests.Implementations
{
    public class InMemorySharedLockDataProvider : ISharedLockDataProviderExtension
    {
        public DataCollection<SharedLockDoc> GetSharedLocks()
        {
            return ((InMemoryDataProvider)DataStore.DataProvider).DB.GetCollection<SharedLockDoc>();
        }


        public TimeSpan SharedLockTimeout { get; } = TimeSpan.FromMinutes(30d);

        public Task DeleteAllSharedLocksAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            GetSharedLocks().Clear();
            return Task.CompletedTask;
        }

        public Task CreateSharedLockAsync(int contentId, string @lock, CancellationToken cancellationToken = default(CancellationToken))
        {
            var sharedLocks = GetSharedLocks();
            var timeLimit = DateTime.UtcNow.AddTicks(-SharedLockTimeout.Ticks);
            var row = sharedLocks.FirstOrDefault(x => x.ContentId == contentId);
            if (row != null && row.CreationDate < timeLimit)
            {
                sharedLocks.Remove(row);
                row = null;
            }

            if (row == null)
            {
                var newSharedLockId = sharedLocks.Count == 0 ? 1 : sharedLocks.Max(t => t.SharedLockId) + 1;
                sharedLocks.Insert(new SharedLockDoc
                {
                    SharedLockId = newSharedLockId,
                    ContentId = contentId,
                    Lock = @lock,
                    CreationDate = DateTime.UtcNow
                });
                return Task.CompletedTask;
            }

            if (row.Lock != @lock)
                throw new LockedNodeException(null, $"The node (#{contentId}) is locked by another shared lock.");

            row.CreationDate = DateTime.UtcNow;
            return Task.CompletedTask;
        }

        public Task<string> RefreshSharedLockAsync(int contentId, string @lock, CancellationToken cancellationToken = default(CancellationToken))
        {
            DeleteTimedOutItems();

            var row = GetSharedLocks().FirstOrDefault(x => x.ContentId == contentId);
            if (row == null)
                throw new SharedLockNotFoundException("Content is unlocked");
            if (row.Lock != @lock)
                throw new LockedNodeException(null, $"The node (#{contentId}) is locked by another shared lock.");
            row.CreationDate = DateTime.UtcNow;
            return Task.FromResult(row.Lock);
        }

        public Task<string> ModifySharedLockAsync(int contentId, string @lock, string newLock, CancellationToken cancellationToken = default(CancellationToken))
        {
            var sharedLocks = GetSharedLocks();

            DeleteTimedOutItems();

            var existingItem = sharedLocks.FirstOrDefault(x => x.ContentId == contentId && x.Lock == @lock);
            if (existingItem != null)
            {
                existingItem.Lock = newLock;
                return Task.FromResult<string>(null);
            }
            var existingLock = sharedLocks.FirstOrDefault(x => x.ContentId == contentId)?.Lock;
            if (existingLock == null)
                throw new SharedLockNotFoundException("Content is unlocked");
            if (existingLock != @lock)
                throw new LockedNodeException(null, $"The node (#{contentId}) is locked by another shared lock.");
            return Task.FromResult(existingLock);
        }

        public Task<string> GetSharedLockAsync(int contentId, CancellationToken cancellationToken = default(CancellationToken))
        {
            var timeLimit = DateTime.UtcNow.AddTicks(-SharedLockTimeout.Ticks);
            return Task.FromResult(GetSharedLocks()
                .FirstOrDefault(x => x.ContentId == contentId && x.CreationDate >= timeLimit)?.Lock);
        }

        public Task<string> DeleteSharedLockAsync(int contentId, string @lock, CancellationToken cancellationToken = default(CancellationToken))
        {
            var sharedLocks = GetSharedLocks();

            DeleteTimedOutItems();

            var existingItem = sharedLocks.FirstOrDefault(x => x.ContentId == contentId && x.Lock == @lock);
            if (existingItem != null)
            {
                sharedLocks.Remove(existingItem);
                return Task.FromResult<string>(null);
            }
            var existingLock = sharedLocks.FirstOrDefault(x => x.ContentId == contentId)?.Lock;
            if (existingLock == null)
                throw new SharedLockNotFoundException("Content is unlocked");
            if (existingLock != @lock)
                throw new LockedNodeException(null, $"The node (#{contentId}) is locked by another shared lock.");
            return Task.FromResult(existingLock);
        }

        public Task CleanupSharedLocksAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            // do nothing
            return Task.CompletedTask;
        }

        public void SetSharedLockCreationDate(int nodeId, DateTime value)
        {
            var sharedLockRow = GetSharedLocks().First(x => x.ContentId == nodeId);
            sharedLockRow.CreationDate = value;
        }

        public DateTime GetSharedLockCreationDate(int nodeId)
        {
            var sharedLockRow = GetSharedLocks().First(x => x.ContentId == nodeId);
            return sharedLockRow.CreationDate;
        }


        private void DeleteTimedOutItems()
        {
            var sharedLocks = GetSharedLocks();

            var timeLimit = DateTime.UtcNow.AddTicks(-SharedLockTimeout.Ticks);
            var timedOutItems = sharedLocks.Where(x => x.CreationDate < timeLimit).ToArray();
            foreach (var item in timedOutItems)
                sharedLocks.Remove(item);
        }
    }
}