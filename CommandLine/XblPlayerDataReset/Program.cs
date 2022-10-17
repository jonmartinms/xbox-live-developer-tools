﻿// Copyright (c) Microsoft Corporation
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace XblPlayerDataReset
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Threading.Tasks;
    using CommandLine;
    using CommandLine.Text;
    using Microsoft.Xbox.Services.DevTools.Authentication;
    using Microsoft.Xbox.Services.DevTools.PlayerReset;

    internal class Program
    {
        private static async Task<int> Main(string[] args)
        {
            try
            {
                ResetOptions options = null;
                var parserResult = Parser.Default.ParseArguments<ResetOptions>(args)
                    .WithParsed<ResetOptions>(parsedOptions => options = parsedOptions);
                if (parserResult.Tag == ParserResultType.NotParsed)
                {
                    return -1;
                }

                return await OnReset(options);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: player data reset failed:");
                Console.WriteLine(ex.Message);
                return -1;
            }
        }

        private static void PrintProviderDetails(List<JobProviderStatus> providers)
        {
            foreach (var provider in providers)
            {
                if (provider.Status == ResetProviderStatus.CompletedSuccess)
                {
                    Console.WriteLine($"\t{provider.Provider}, Status: {provider.Status} " +
                                      (provider.Status == ResetProviderStatus.CompletedSuccess? $"Error: {provider.ErrorMessage}" : string.Empty));
                }
            }
        }

        private static async Task<int> OnReset(ResetOptions options)
        {
            // TODO: Add a maximum?
            List<string> xuids = new List<string>();

            if (options == null)
            {
                Console.WriteLine("Unknown parameter error");
                return -1;
            }

            if (!string.IsNullOrEmpty(options.TestAccount))
            {
                TestAccount testAccount = await ToolAuthentication.SignInTestAccountAsync(options.TestAccount, options.Sandbox);
                if (testAccount == null)
                {
                    Console.Error.WriteLine($"Failed to log in to test account {options.TestAccount}.");
                    return -1;
                }
                // TODO: Extract email list
                xuids = new List<string>() { testAccount.Xuid };

                Console.WriteLine($"Using Test account(s) {options.TestAccount} ({testAccount.Gamertag}) with xuid {options.XboxUserId}");
            }
            else if (!string.IsNullOrEmpty(options.XboxUserId))
            {
                DevAccount account = ToolAuthentication.LoadLastSignedInUser();
                if (account == null)
                {
                    Console.Error.WriteLine("Resetting by XUID requires a signed in Partner Center account. Please use \"XblDevAccount.exe signin\" to log in.");
                    return -1;
                }

                xuids = options.XboxUserId.Split(',').ToList();

                Console.WriteLine($"Using Dev account(s) {account.Name} from {account.AccountSource}");
            }

            Console.WriteLine($"Resetting data for player with XUID(s) {options.XboxUserId} for SCID {options.ServiceConfigurationId} in sandbox {options.Sandbox}");

            try
            {
                UserResetResult result = await PlayerReset.ResetPlayerDataAsync(
                    options.ServiceConfigurationId,
                    options.Sandbox, xuids);

                switch (result.OverallResult)
                {
                    case ResetOverallResult.Succeeded:
                        Console.WriteLine("Player data has been reset successfully.");
                        return 0;
                    case ResetOverallResult.CompletedError:
                        Console.WriteLine("An error occurred while resetting player data:");
                        if (!string.IsNullOrEmpty(result.HttpErrorMessage))
                        {
                            Console.WriteLine($"\t{result.HttpErrorMessage}");
                        }

                        PrintProviderDetails(result.ProviderStatus);
                        return -1;
                    case ResetOverallResult.Timeout:
                        Console.WriteLine("Player data reset has timed out:");
                        PrintProviderDetails(result.ProviderStatus);
                        return -1;
                    default:
                        Console.WriteLine("An unknown error occurred while resetting player data.");
                        return -1;
                }
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine("Error: player data reset failed");

                if (ex.Message.Contains(Convert.ToString((int)HttpStatusCode.Unauthorized)))
                {
                    Console.WriteLine(
                        $"Unable to authorize the account with Xbox Live and scid : {options.ServiceConfigurationId} and sandbox : {options.Sandbox}, please contact your administrator.");
                }
                else if (ex.Message.Contains(Convert.ToString((int) HttpStatusCode.Forbidden)))
                {
                    Console.WriteLine(
                        "Your account doesn't have access to perform the operation, please contact your administrator.");
                }
                else
                {
                    Console.WriteLine(ex.Message);
                }

                return -1;
            }
        }

        internal class ResetOptions
        {
            [Option('c', "scid", Required = true,
                HelpText = "The service configuration ID (SCID) of the title for player data resetting")]
            public string ServiceConfigurationId { get; set; }

            [Option('s', "sandbox", Required = true,
                HelpText = "The target sandbox for player resetting")]
            public string Sandbox { get; set; }

            [Option('x', "xuid", Required = false, SetName = "xuid",
                // TODO: Owner? Admin? Who is allowed to run this?
                HelpText = "A list of Xbox Live User IDs (XUID) of the players to be reset. Requires login of xuid owner.")]
            public string XboxUserId { get; set; }

            [Option('u', "user", Required = false, SetName = "testacct",
                HelpText = "A list of email addresses of the test accounts to be reset. Requires password input per account.")]
            public string TestAccount { get; set; }

            [Usage(ApplicationAlias = "XblPlayerDataReset")]
            public static IEnumerable<Example> Examples
            {
                get
                {
                    // TODO: Update usage example
                    yield return new Example("Reset a player for given scid and sandbox", new ResetOptions { ServiceConfigurationId = "xxx", Sandbox = "xxx", XboxUserId = "xxx", TestAccount = "xxx@xboxtest.com" });
                }
            }
        }
    }
}
