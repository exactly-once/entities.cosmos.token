﻿using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Azure.Storage.Blobs;

namespace ExactlyOnce.ClaimCheck.BlobStore
{
    public class BlobMessageStore : IMessageStore
    {
        BlobContainerClient containerClient;

        public BlobMessageStore(BlobContainerClient containerClient)
        {
            this.containerClient = containerClient;
        }

        public Task Delete(string messageId)
        {
            return containerClient.GetBlobClient(messageId).DeleteAsync();
        }

        public async Task<byte[]> TryGet(string id)
        {
            var response = await containerClient.GetBlobClient(id).DownloadAsync().ConfigureAwait(false);
            var buffer = new byte[response.Value.ContentLength];
            using (var reader = new BinaryReader(response.Value.Content))
            {
                reader.Read(buffer, 0, buffer.Length);
            }
            return buffer;
        }

        public async Task<bool> CheckExists(string id)
        {
            var response = await containerClient.GetBlobClient(id).ExistsAsync().ConfigureAwait(false);
            return response.Value;
        }

        public Task Create(Message[] messages)
        {
            var tasks = messages.Select(async m =>
            {
                using (var stream = new MemoryStream(m.Body))
                {
                    await containerClient.GetBlobClient(m.Id).UploadAsync(stream).ConfigureAwait(false);
                }
            });

            return Task.WhenAll(tasks);
        }
    }
}