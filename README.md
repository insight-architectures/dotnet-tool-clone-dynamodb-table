# Insight Architectures Clone DynamoDB Table .NET Tool

This .NET global tool can be used to clone an AWS DynamoDB table and its content from one account into another or into a local instance.

## Installation

To install this tool, simply execute the following command in your terminal of choice

```sh
$ dotnet tool install InsightArchitectures.Tools.CloneDynamoDbTable
```

Note that you'll need a tool manifest to be able to install any tool locally.

Alternatively, you can install the tool globally using the `-g` switch

```sh
$ dotnet tool install -g InsightArchitectures.Tools.CloneDynamoDbTable
```

## Usage

Once installed, either locally or globally, you can use the command by running the following command

```sh
$ dotnet clone-dynamodb-table [options]
```

The tool can be configured using the following options

|Option|Description|Required|Default|
|-|-|-|-|
|`--source-table-name <source-table-name>`|The source table to copy.|yes||
|`--source-profile <source-profile>`|The profile to be used to access the source table.|no||
|`--source-region <source-region>`|The region where the source table is located.|no||
|`--source-service-url <source-service-url>`|The service url to be used to access the source table.|no||
|`--source-access-key <source-access-key>`|The access key to be used to access the source table.|no||
|`--source-secret-key <source-secret-key>`|The secret key to be used to access the source table.|no||
|`--target-table-name <target-table-name>`|The target table to copy.|no||
|`--target-profile <target-profile>`|The profile to be used to access the target table.|no||
|`--target-region <target-region>`|The region where the target table is located.|no||
|`--target-service-url <target-service-url>`|The service url to be used to access the target table.|no||
|`--target-access-key <target-access-key>`|The access key to be used to access the target table.|no||
|`--target-secret-key <target-secret-key>`|The secret key to be used to access the target table.|no||
|`--settings-file-path <settings-file-path>`|The path to a JSON file containing settings to be used.|no||
|`--clone-content`|Specifies whether the content of the source table should be cloned in the target table.|no|`False`|
