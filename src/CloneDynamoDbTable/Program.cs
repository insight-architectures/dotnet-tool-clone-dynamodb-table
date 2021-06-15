using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Extensions.NETCore.Setup;
using Amazon.Runtime;
using InsightArchitectures.Utilities;
using Microsoft.Extensions.Configuration;

namespace CloneDynamoDbTable
{
    class Program
    {
        static async Task Main(string tableName, string targetTableName = null, string settingsFilePath = null, bool cloneContent = false)
        {
            var configurationBuilder = new ConfigurationBuilder();

            if (settingsFilePath is not null)
            {
                configurationBuilder.AddJsonFile(settingsFilePath);
            }

            configurationBuilder.AddEnvironmentVariables();

            var configuration = configurationBuilder.Build();

            var source = GetOptions(configuration, "Source");

            var target = GetOptions(configuration, "Target");

            var sourceClient = source.CreateServiceClient<IAmazonDynamoDB>();

            var targetClient = target.CreateServiceClient<IAmazonDynamoDB>();

            var manager = new TableManager(sourceClient, targetClient);

            try
            {
                targetTableName ??= tableName;

                Console.WriteLine($"Copying {tableName} into table {targetTableName}");

                await manager.CopyTableAsync(tableName, targetTableName);

                if (cloneContent)
                {
                    Console.WriteLine($"Cloning {tableName} content into table {targetTableName}");

                    await manager.CloneContentAsync(tableName, targetTableName);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
            }
            
        }

        private static AWSOptions GetOptions(IConfiguration configuration, string sectionName)
        {
            var section = configuration.GetSection(sectionName);

            if (section == null)
            {
                throw new Exception($"{sectionName} configuration not found");
            }

            var options = configuration.GetAWSOptions(sectionName);

            var credentials = section.GetSection("Credentials");

            if (credentials != null && credentials.GetChildren().Any())
            {
                options.Credentials = new BasicAWSCredentials(credentials["AccessKey"], credentials["SecretKey"]);
            }

            return options;
        }
    }

    public class TableManager
    {
        private readonly IAmazonDynamoDB _source;
        private readonly IAmazonDynamoDB _target;

        public TableManager(IAmazonDynamoDB source, IAmazonDynamoDB target)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _target = target ?? throw new ArgumentNullException(nameof(target));
        }

        public async Task CopyTableAsync(string tableName, string targetTableName)
        {
            var sourceTable = await GetTableInformation(_source, tableName) ?? throw new Exception("Source table doesn't exist");

            if (!await CheckIfTableExists(_target, targetTableName))
            {
                var response = await _target.CreateTableAsync(new CreateTableRequest
                {
                    TableName = targetTableName,
                    AttributeDefinitions = sourceTable.AttributeDefinitions,
                    BillingMode = sourceTable.BillingModeSummary.BillingMode,
                    GlobalSecondaryIndexes = (from index in sourceTable.GlobalSecondaryIndexes
                                              select new GlobalSecondaryIndex
                                              {
                                                  IndexName = index.IndexName,
                                                  KeySchema = index.KeySchema,
                                                  Projection = index.Projection,
                                                  ProvisionedThroughput = new ProvisionedThroughput
                                                  {
                                                      ReadCapacityUnits = index.ProvisionedThroughput.ReadCapacityUnits,
                                                      WriteCapacityUnits = index.ProvisionedThroughput.WriteCapacityUnits
                                                  }
                                              }).ToList(),
                    LocalSecondaryIndexes = (from index in sourceTable.LocalSecondaryIndexes
                                             select new LocalSecondaryIndex
                                             {
                                                 IndexName = index.IndexName,
                                                 KeySchema = index.KeySchema,
                                                 Projection = index.Projection
                                             }).ToList(),
                    KeySchema = sourceTable.KeySchema,
                    ProvisionedThroughput = sourceTable.ProvisionedThroughput switch
                    {
                        null => null,
                        var x => new ProvisionedThroughput
                        {
                            ReadCapacityUnits = x.ReadCapacityUnits,
                            WriteCapacityUnits = x.WriteCapacityUnits
                        }
                    },
                    StreamSpecification = sourceTable.StreamSpecification
                });
            }
            else
            {
                Console.WriteLine($"Table {targetTableName} already exists. Skipping.");
            }
        }

        public async Task CloneContentAsync(string tableName, string targetTableName)
        {
            if (!await CheckIfTableExists(_source, tableName))
            {
                throw new Exception("Source table doesn't exist");
            }

            if (!await CheckIfTableExists(_target, targetTableName))
            {
                throw new Exception("Target table doesn't exist");
            }

            var scanRequest = new ScanRequest
            {
                TableName = tableName
            };

            var writeRequests = _source.Paginators
                                       .Scan(scanRequest)
                                       .Responses
                                       .ToEnumerable()
                                       .SelectMany(i => i.Items)
                                       .Select(item => new WriteRequest(new PutRequest { Item = item }))
                                       .Paginate(25)
                                       .Select(page => new BatchWriteItemRequest
                                       {
                                           RequestItems = new Dictionary<string, List<WriteRequest>>
                                           {
                                               [targetTableName] = new List<WriteRequest>(page)
                                           }
                                       });

            foreach (var request in writeRequests)
            {
                await _target.BatchWriteItemAsync(request);
            }
        }

        private static async Task<TableDescription> GetTableInformation(IAmazonDynamoDB client, string tableName)
        {
            try
            {
                var response = await client.DescribeTableAsync(tableName);

                return response.Table;
            }
            catch (ResourceNotFoundException)
            {
                return null;
            }
        }

        private static async Task<bool> CheckIfTableExists(IAmazonDynamoDB client, string tableName) => await GetTableInformation(client, tableName) != null;
    }
}
