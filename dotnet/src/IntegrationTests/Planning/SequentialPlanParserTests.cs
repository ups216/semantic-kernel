﻿// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Planning;
using SemanticKernel.IntegrationTests.Fakes;
using SemanticKernel.IntegrationTests.TestSettings;
using Xunit;
using Xunit.Abstractions;

namespace SemanticKernel.IntegrationTests.Planning;

public class SequentialPlanParserTests
{
    public SequentialPlanParserTests(ITestOutputHelper output)
    {
        // Load configuration
        this._configuration = new ConfigurationBuilder()
            .AddJsonFile(path: "testsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile(path: "testsettings.development.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables()
            .AddUserSecrets<SequentialPlanParserTests>()
            .Build();
    }

    [Fact]
    public void CanCallToPlanFromXml()
    {
        // Arrange
        AzureOpenAIConfiguration? azureOpenAIConfiguration = this._configuration.GetSection("AzureOpenAI").Get<AzureOpenAIConfiguration>();
        Assert.NotNull(azureOpenAIConfiguration);

        IKernel kernel = Kernel.Builder
            .Configure(config =>
            {
                config.AddAzureTextCompletionService(
                    serviceId: azureOpenAIConfiguration.ServiceId,
                    deploymentName: azureOpenAIConfiguration.DeploymentName,
                    endpoint: azureOpenAIConfiguration.Endpoint,
                    apiKey: azureOpenAIConfiguration.ApiKey);
                config.SetDefaultTextCompletionService(azureOpenAIConfiguration.ServiceId);
            })
            .Build();
        kernel.ImportSkill(new EmailSkillFake(), "email");
        var summarizeSkill = TestHelpers.GetSkill("SummarizeSkill", kernel);
        var writerSkill = TestHelpers.GetSkill("WriterSkill", kernel);

        var planString =
            @"<goal>
Summarize an input, translate to french, and e-mail to John Doe
</goal>
<plan>
    <function.SummarizeSkill.Summarize/>
    <function.WriterSkill.Translate language=""French"" setContextVariable=""TRANSLATED_SUMMARY""/>
    <function.email.GetEmailAddressAsync input=""John Doe"" setContextVariable=""EMAIL_ADDRESS""/>
    <function.email.SendEmailAsync input=""$TRANSLATED_SUMMARY"" email_address=""$EMAIL_ADDRESS""/>
</plan>";

        // Act
        var plan = planString.ToPlanFromXml(kernel.CreateNewContext());

        // Assert
        Assert.NotNull(plan);
        Assert.Equal("Summarize an input, translate to french, and e-mail to John Doe", plan.Description);

        Assert.Equal(4, plan.Steps.Count);
        Assert.Collection(plan.Steps,
            step =>
            {
                Assert.Equal("SummarizeSkill", step.SkillName);
                Assert.Equal("Summarize", step.Name);
            },
            step =>
            {
                Assert.Equal("WriterSkill", step.SkillName);
                Assert.Equal("Translate", step.Name);
                Assert.Equal("French", step.NamedParameters["language"]);
                Assert.True(step.NamedOutputs.ContainsKey("TRANSLATED_SUMMARY"));
            },
            step =>
            {
                Assert.Equal("email", step.SkillName);
                Assert.Equal("GetEmailAddressAsync", step.Name);
                Assert.Equal("John Doe", step.NamedParameters["input"]);
                Assert.True(step.NamedOutputs.ContainsKey("EMAIL_ADDRESS"));
            },
            step =>
            {
                Assert.Equal("email", step.SkillName);
                Assert.Equal("SendEmailAsync", step.Name);
                Assert.Equal("$TRANSLATED_SUMMARY", step.NamedParameters["input"]);
                Assert.Equal("$EMAIL_ADDRESS", step.NamedParameters["email_address"]);
            }
        );
    }

    private readonly IConfigurationRoot _configuration;
}
