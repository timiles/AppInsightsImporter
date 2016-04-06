﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BlobExporter.Models;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace BlobExporter
{
    internal class BlobStorageClient
    {
        private CloudStorageAccount _storageAccount;
        private readonly string _containerName;
        private readonly string _appName;
        private readonly string _instrumentationKey;

        internal BlobStorageClient(
            string connectionString,
            string containerName,
            string appName,
            Guid instrumentationKey)
        {
            _storageAccount = CloudStorageAccount.Parse(connectionString);
            _containerName = containerName.ToLower();
            _appName = appName.ToLower();
            _instrumentationKey = instrumentationKey.ToString().Replace("-", "").ToLower();
        }

        internal IEnumerable<BlobInfo> DownloadExceptionsSince(DateTimeOffset sinceUtcDateTime)
        {
            var blobClient = _storageAccount.CreateCloudBlobClient();
            var containerReference = blobClient.GetContainerReference(_containerName);

            for (var listBlobsCreatedOnDate = sinceUtcDateTime.Date;
                listBlobsCreatedOnDate <= DateTime.UtcNow.Date;
                listBlobsCreatedOnDate = listBlobsCreatedOnDate.AddDays(1))
            {
                var prefix =
                    $@"{this._appName}_{this._instrumentationKey}/Exceptions/{listBlobsCreatedOnDate.ToString("yyyy-MM-dd")}/";
                // use flat blob listing to reduce calls to API
                foreach (var blob in containerReference.ListBlobs(prefix, useFlatBlobListing: true))
                {
                    // should all be CloudBlockBlobs, since we've used flat blot listing.
                    var cloudBlockBlob = blob as CloudBlockBlob;
                    if (cloudBlockBlob != null && cloudBlockBlob.Properties.LastModified > sinceUtcDateTime)
                    {
                        yield return DownloadBlob(blobClient, cloudBlockBlob);
                    }
                }
            }
        }

        private static BlobInfo DownloadBlob(CloudBlobClient blobClient, CloudBlockBlob blob)
        {
            var cloudBlockBlob = blobClient.GetBlobReferenceFromServer(blob.Uri);
            using (var memoryStream = new MemoryStream())
            {
                cloudBlockBlob.DownloadToStream(memoryStream);
                var content  = Encoding.UTF8.GetString(memoryStream.ToArray());
                return new BlobInfo
                {
                    Path = blob.Uri.LocalPath,
                    LastModified = blob.Properties.LastModified,
                    Content = content
                };
            }
        }

        internal void DeleteBlob(string path)
        {
            var containerPrefix = '/' + _containerName + '/';
            if (!path.StartsWith(containerPrefix))
            {
                throw new ArgumentException($"Path should be reference to a blob in container \"{_containerName}\"");
            }

            var blobName = path.Substring(containerPrefix.Length);

            var blobClient = _storageAccount.CreateCloudBlobClient();
            var containerReference = blobClient.GetContainerReference(_containerName);
            var blob = containerReference.GetBlockBlobReference(blobName);
            blob.DeleteIfExists();
        }
    }
}