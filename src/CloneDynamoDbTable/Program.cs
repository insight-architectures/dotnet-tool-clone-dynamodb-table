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
        /// <summary>
        /// A tool to copy a AWS DynamoDB table and optionally its content from an account to another.
        /// </summary>
        /// <param name="sourceTableName">The source table to copy.</param>
        /// <param name="sourceProfile">The profile to be used to access the source table.</param>
        /// <param name="sourceRegion">The region where the source table is located.</param>
        /// <param name="sourceServiceUrl">The service url to be used to access the source table.</param>
        /// <param name="sourceAccessKey">The access key to be used to access the source table.</param>
        /// <param name="sourceSecretKey">The secret key to be used to access the source table.</param>
        /// <param name="targetTableName">The target table to copy.</param>
        /// <param name="targetProfile">The profile to be used to access the target table.</param>
        /// <param name="targetRegion">The region where the target table is located.</param>
        /// <param name="targetServiceUrl">The service url to be used to access the target table.</param>
        /// <param name="targetAccessKey">The access key to be used to access the target table.</param>
        /// <param name="targetSecretKey">The secret key to be used to access the target table.</param>
        /// <param name="settingsFilePath">The path to a JSON file containing settings to be used.</param>
        /// <param name="cloneContent">Specifies whether the content of the source table should be cloned in the target table.</param>
        public static async Task Main(
            string sourceTableName,
            string sourceProfile = null,
            string sourceRegion = null,
            Uri sourceServiceUrl = null,
            string sourceAccessKey = null,
            string sourceSecretKey = null,
            string targetTableName = null,
            string targetProfile = null,
            string targetRegion = null,
            Uri targetServiceUrl = null,
            string targetAccessKey = null,
            string targetSecretKey = null,
            string settingsFilePath = null,
            bool cloneContent = false
        )
        {
            var configurationBuilder = new ConfigurationBuilder();

            if (settingsFilePath is not null)
            {
                configurationBuilder.AddJsonFile(settingsFilePath);
            }

            configurationBuilder.AddObject(new
            {
                Source = new
                {
                    Profile = sourceProfile,
                    Region = sourceRegion,
                    ServiceURL = sourceServiceUrl,
                    Credentials = new
                    {
                        AccessKey = sourceAccessKey,
                        SecretKey = sourceSecretKey
                    }
                },
                Target = new
                {
                    Profile = targetProfile,
                    Region = targetRegion,
                    ServiceURL = targetServiceUrl,
                    Credentials = new
                    {
                        AccessKey = targetAccessKey,
                        SecretKey = targetSecretKey
                    }
                }
            });

            var configuration = configurationBuilder.Build();
            
            var source = GetOptions(configuration, "Source");

            var target = GetOptions(configuration, "Target");

            var sourceClient = source.CreateServiceClient<IAmazonDynamoDB>();

            var targetClient = target.CreateServiceClient<IAmazonDynamoDB>();

            try
            {
                targetTableName ??= sourceTableName;

                Console.WriteLine($"Copying {sourceTableName} into table {targetTableName}");

                await new DuplicateTableCommand(sourceClient, targetClient, sourceTableName, targetTableName).ExecuteAsync();

                if (cloneContent)
                {
                    Console.WriteLine($"Cloning {sourceTableName} content into table {targetTableName}");

                    await new CloneTableContentCommand(sourceClient, targetClient, sourceTableName, targetTableName).ExecuteAsync();
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

    internal class DuplicateTableCommand
    {
        private readonly IAmazonDynamoDB _source;
        private readonly IAmazonDynamoDB _target;
        private readonly string _tableName;
        private readonly string _targetTableName;

        public DuplicateTableCommand(IAmazonDynamoDB source, IAmazonDynamoDB target, string tableName, string targetTableName)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _target = target ?? throw new ArgumentNullException(nameof(target));
            _tableName = tableName ?? throw new ArgumentNullException(nameof(tableName));
            _targetTableName = targetTableName ?? throw new ArgumentNullException(nameof(targetTableName));
        }

        public async Task ExecuteAsync()
        {
            var sourceTable = await _source.GetTableInformation(_tableName) ?? throw new Exception("Source table doesn't exist");

            if (!await _target.CheckIfTableExists(_targetTableName))
            {
                var response = await _target.CreateTableAsync(new CreateTableRequest
                {
                    TableName = _targetTableName,
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
                Console.WriteLine($"Table {_targetTableName} already exists. Skipping.");
            }
        }
    }

    internal class CloneTableContentCommand
    {
        private readonly IAmazonDynamoDB _source;
        private readonly IAmazonDynamoDB _target;
        private readonly string _tableName;
        private readonly string _targetTableName;

        public CloneTableContentCommand(IAmazonDynamoDB source, IAmazonDynamoDB target, string tableName, string targetTableName)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _target = target ?? throw new ArgumentNullException(nameof(target));
            _tableName = tableName ?? throw new ArgumentNullException(nameof(tableName));
            _targetTableName = targetTableName ?? throw new ArgumentNullException(nameof(targetTableName));
        }

        public async Task ExecuteAsync()
        {
            if (!await _source.CheckIfTableExists(_tableName))
            {
                throw new Exception("Source table doesn't exist");
            }

            if (!await _target.CheckIfTableExists(_targetTableName))
            {
                throw new Exception("Target table doesn't exist");
            }

            var scanRequest = new ScanRequest
            {
                TableName = _tableName
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
                                               [_targetTableName] = new List<WriteRequest>(page)
                                           }
                                       });

            foreach (var request in writeRequests)
            {
                await _target.BatchWriteItemAsync(request);
            }
        }
    }

    internal static class DynamoDbClientHelpers
    {
        public static async Task<TableDescription> GetTableInformation(this IAmazonDynamoDB client, string tableName)
        {
            _ = client ?? throw new ArgumentNullException(nameof(client));

            _ = tableName ?? throw new ArgumentNullException(nameof(tableName));

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

        public static async Task<bool> CheckIfTableExists(this IAmazonDynamoDB client, string tableName) => await GetTableInformation(client, tableName) != null;
    }
}
