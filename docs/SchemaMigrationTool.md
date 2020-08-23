# SQL Server schema Migration Tool
The SQL Server schema migration tool is the command line utility to perform SQL Server schema upgrade on demand. The tool will automatically identify the current version of the database in order to apply appropriate migration scripts.

Note - The tool can't downgrade a schema version.

- #### Prerequisites
    The tool would only run when the current schema is in conformance to the [BaseSchema](BaseSchema.md). So either the BaseSchema should already be present or if not, then Schema admin would need to run BaseSchema script manually.

- #### How to install the tool

    - ##### If available as .NET Core global tool 

        It can be installed like this:

        - Open a terminal/command prompt 
        - Type 'dotnet tool install -g Microsoft.Health.SchemaManager'

     - ##### If not available as .NET core global tool, then install it from public feed

        It can be installed like this:
            
        - Visual Studio setup - Start Visual Studio as admin. On the Tools menu, select Options > NuGet Package Manager > Package Sources. Select the green plus in the upper-right corner and enter the name and source URL as below:

                Name: "Microsoft Health OSS"
                Source: https://microsofthealthoss.pkgs.visualstudio.com/FhirServer/_packaging/Public/nuget/v3/index.json
        
        - In the Package Manager Console, type the below command
        
                PM> dotnet tool install -g Microsoft.Health.SchemaManager --version [latestversion]

- #### How to uninstall the tool
    In any case, if the tool needs to be uninstalled, the command is as follows

        PM> dotnet tool uninstall -g Microsoft.Health.SchemaManager          

- #### Commands
    The tool supports following commands:

    |Command|Description|Options|Usage
    |--------|---|---|---|
    |current|Returns the current versions from the SchemaVersion table along with information on the instances using the given version|--server/-s|schema-manager current [options]
    |available|Returns the versions greater than or equal to the current version along with the path to the T-SQL scripts for upgrades|--server/-s|schema-manager available [options]
    |apply|Applies the specified version(s) to the connection string supplied. Optionally can poll the FHIR server current version to apply multiple versions in sequence|--server/-s,<br /> --connection-string/-cs,<br /> --next/-n,<br /> --version/-v,<br /> --latest/-l,<br /> --force/-f|schema-manager apply [options]

- #### Options 

    |Option|Description|Usage
    |--------|---|---|
    |--server/-s|To provide the host url of the application| --server https://localhost:12345|
    --connection-string/-cs| To provide the connection string  of the sql database| --connection-string "server=(local);Initial Catalog=DATABASE_NAME;Integrated Security=true"|
    --next/-n| It fetches the available versions and apply the next immediate available version to the current version| ---next|
    --version/-v|To provide the schema version to upgrade. It applies all the versions between current version and the specified version|--version 5|
    --latest/-l|It fetches the available versions and apply all the versions between current and the latest available version|--latest|
    --force/-f|This option can be used with --next, --version and --latest. It skips all the checks to validate version and forces the tool to perform schema migration|--force
