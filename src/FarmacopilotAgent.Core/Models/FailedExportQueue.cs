using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace FarmacopilotAgent.Core.Models
{
    public class FailedExportQueue
    {
        private readonly string _queueFilePath;
        private readonly Queue<FailedExportItem> _queue;

        public FailedExportQueue(string basePath)
        {
            _queueFilePath = Path.Combine(basePath, "failed_exports.json");
            _queue = new Queue<FailedExportItem>();
            LoadQueue();
        }

        public async Task EnqueueAsync(string tableName, DateTime? lastExport, string error)
        {
            var item = new FailedExportItem
            {
                TableName = tableName,
                LastExportTimestamp = lastExport,
                FailedAt = DateTime.UtcNow,
                ErrorMessage = error,
                RetryCount = 0
            };

            _queue.Enqueue(item);
            await SaveQueueAsync();
        }

        public async Task<FailedExportItem?> DequeueAsync()
        {
            if (_queue.Count == 0) return null;
            
            var item = _queue.Dequeue();
            await SaveQueueAsync();
            return item;
        }

        public async Task RequeueAsync(FailedExportItem item)
        {
            item.RetryCount++;
            item.NextRetryAt = DateTime.UtcNow.AddMinutes(Math.Pow(2, item.RetryCount));
            
            if (item.RetryCount < 5) // Max 5 reintentos
            {
                _queue.Enqueue(item);
                await SaveQueueAsync();
            }
        }

        public int Count => _queue.Count;

        private void LoadQueue()
        {
            if (!File.Exists(_queueFilePath)) return;
            
            var json = File.ReadAllText(_queueFilePath);
            var items = JsonSerializer.Deserialize<List<FailedExportItem>>(json);
            
            if (items != null)
            {
                foreach (var item in items)
                {
                    if (item.NextRetryAt == null || item.NextRetryAt <= DateTime.UtcNow)
                        _queue.Enqueue(item);
                }
            }
        }

        private async Task SaveQueueAsync()
        {
            var items = _queue.ToArray();
            var json = JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_queueFilePath, json);
        }
    }

    public class FailedExportItem
    {
        public string TableName { get; set; } = string.Empty;
        public DateTime? LastExportTimestamp { get; set; }
        public DateTime FailedAt { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public int RetryCount { get; set; }
        public DateTime? NextRetryAt { get; set; }
    }
}
