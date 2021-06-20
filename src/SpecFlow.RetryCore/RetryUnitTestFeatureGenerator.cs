// -----------------------------------------------------------------------
// <copyright file="RetryUnitTestFeatureGenerator.cs" company="Calrom Ltd.">
// Under MIT license
// </copyright>
// -----------------------------------------------------------------------

namespace CalromSpecFlowRetryCore
{
    using System;
    using System.CodeDom;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Text.RegularExpressions;
    using Gherkin.Ast;
    using TechTalk.SpecFlow;
    using TechTalk.SpecFlow.Configuration;
    using TechTalk.SpecFlow.Generator;
    using TechTalk.SpecFlow.Generator.CodeDom;
    using TechTalk.SpecFlow.Generator.UnitTestConverter;
    using TechTalk.SpecFlow.Generator.UnitTestProvider;
    using TechTalk.SpecFlow.Parser;
    using TechTalk.SpecFlow.Tracing;

    public class RetryUnitTestFeatureGenerator : IFeatureGenerator
    {
        private const string DEFAULTNAMESPACE = "SpecFlowTests";
        private const string TESTCLASSNAMEFORMAT = "{0}Feature";
        private const string TESTNAMEFORMAT = "{0}";
        private const string SCENARIOINITIALIZENAME = "ScenarioSetup";
        private const string SCENARIOCLEANUPNAME = "ScenarioCleanup";
        private const string TESTINITIALIZENAME = "TestInitialize";
        private const string TESTCLEANUPNAME = "ScenarioTearDown";
        private const string TESTCLASSINITIALIZENAME = "FeatureSetup";
        private const string TESTCLASSCLEANUPNAME = "FeatureTearDown";
        private const string BACKGROUNDNAME = "FeatureBackground";
        private const string TESTRUNNERFIELD = "testRunner";
        private const string SPECFLOWNAMESPACE = "TechTalk.SpecFlow";

        private readonly IUnitTestGeneratorProvider testGeneratorProvider;
        private readonly SpecFlowConfiguration specFlowConfiguration;
        private readonly CodeDomHelper codeDomHelper;
        private readonly IDecoratorRegistry decoratorRegistry;
        private readonly ITagFilterMatcher tagFilterMatcher;

        private int tableCounter = 0;

        /// <summary>
        /// Initialises a new instance of the <see cref="RetryUnitTestFeatureGenerator"/> class.
        /// </summary>
        /// <param name="testGeneratorProvider">testGenerator Provider.</param>
        /// <param name="codeDomHelper">codeDom Helper.</param>
        /// <param name="specFlowConfiguration">specFlowConfiguration. </param>
        /// <param name="decoratorRegistry">decorator Registry.</param>
        /// <param name="tagFilterMatcher">tagFilter Matcher.</param>
        public RetryUnitTestFeatureGenerator(
            IUnitTestGeneratorProvider testGeneratorProvider,
            CodeDomHelper codeDomHelper,
            SpecFlowConfiguration specFlowConfiguration,
            IDecoratorRegistry decoratorRegistry,
            ITagFilterMatcher tagFilterMatcher)
        {
            this.testGeneratorProvider = testGeneratorProvider;
            this.specFlowConfiguration = specFlowConfiguration;
            this.codeDomHelper = codeDomHelper;
            this.decoratorRegistry = decoratorRegistry;
            this.tagFilterMatcher = tagFilterMatcher;
        }

        private delegate bool TryParseDelegate<T>(string value, out T parsedValue);

        public CodeNamespace GenerateUnitTestFixture(
            SpecFlowDocument document,
            string testClassName,
            string targetNamespace)
        {
            CodeNamespace codeNamespace = CreateNamespace(targetNamespace);
            var feature = document.SpecFlowFeature;

            testClassName ??= string.Format(TESTCLASSNAMEFORMAT, feature.Name.ToIdentifier());
            var generationContext = this.CreateTestClassStructure(codeNamespace, testClassName, document);

            this.SetupTestClass(generationContext);
            this.SetupTestClassInitializeMethod(generationContext);
            this.SetupTestClassCleanupMethod(generationContext);

            SetupScenarioInitializeMethod(generationContext);
            this.SetupFeatureBackground(generationContext);
            SetupScenarioCleanupMethod(generationContext);

            this.SetupTestInitializeMethod(generationContext);
            this.SetupTestCleanupMethod(generationContext);

            foreach (var scenarioDefinition in feature.ScenarioDefinitions)
            {
                if (string.IsNullOrEmpty(scenarioDefinition.Name))
                {
                    throw new TestGeneratorException("The scenario must have a title specified.");
                }

                this.GenerateTest(generationContext, (Scenario)scenarioDefinition);
            }

            this.testGeneratorProvider.FinalizeTestClass(generationContext);
            return codeNamespace;
        }

        private static bool HasFeatureBackground(SpecFlowFeature feature)
        {
            return feature.Background != null;
        }

        private static string GetTestMethodName(
            Scenario scenario,
            string variantName,
            string exampleSetIdentifier)
        {
            var methodName = string.Format(TESTNAMEFORMAT, scenario.Name.ToIdentifier());

            if (variantName != null)
            {
                var variantNameIdentifier = variantName.ToIdentifier().TrimStart('_');
                methodName = string.IsNullOrEmpty(exampleSetIdentifier)
                    ? string.Format("{0}_{1}", methodName, variantNameIdentifier)
                    : string.Format("{0}_{1}_{2}", methodName, exampleSetIdentifier, variantNameIdentifier);
            }

            return methodName;
        }

        private static CodeMemberMethod CreateMethod(CodeTypeDeclaration type)
        {
            CodeMemberMethod method = new CodeMemberMethod();
            type.Members.Add(method);
            return method;
        }

        private static CodeNamespace CreateNamespace(string targetNamespace)
        {
            targetNamespace ??= DEFAULTNAMESPACE;

            CodeNamespace codeNamespace = new CodeNamespace(targetNamespace);

            codeNamespace.Imports.Add(new CodeNamespaceImport(SPECFLOWNAMESPACE));
            return codeNamespace;
        }

        private static CodeMemberField DeclareTestRunnerMember(CodeTypeDeclaration type)
        {
            CodeMemberField testRunnerField = new CodeMemberField(typeof(ITestRunner), TESTRUNNERFIELD);
            type.Members.Add(testRunnerField);
            return testRunnerField;
        }

        private static CodeExpression GetTestRunnerExpression()
        {
            return new CodeVariableReferenceExpression(TESTRUNNERFIELD);
        }

        private static IEnumerable<Tag> ConcatTags(params IEnumerable<Tag>[] tagLists)
        {
            return tagLists.Where(tagList => tagList != null).SelectMany(tagList => tagList);
        }

        private static SpecFlowStep AsSpecFlowStep(Step step)
        {
            if (!(step is SpecFlowStep specFlowStep))
            {
                throw new TestGeneratorException("The step must be a SpecFlowStep.");
            }

            return specFlowStep;
        }

        private static CodeExpression GetSubstitutedString(string text, ParameterSubstitution paramToIdentifier)
        {
            if (text == null)
            {
                return new CodeCastExpression(typeof(string), new CodePrimitiveExpression(null));
            }

            if (paramToIdentifier == null)
            {
                return new CodePrimitiveExpression(text);
            }

            Regex paramRe = new Regex(@"\<(?<param>[^\>]+)\>");
            string formatText = text.Replace("{", "{{").Replace("}", "}}");
            List<string> arguments = new List<string>();

            formatText = paramRe.Replace(formatText, match =>
            {
                string param = match.Groups["param"].Value;

                if (!paramToIdentifier.TryGetIdentifier(param, out string id))
                {
                    return match.Value;
                }

                int argIndex = arguments.IndexOf(id);

                if (argIndex < 0)
                {
                    argIndex = arguments.Count;
                    arguments.Add(id);
                }

                return "{" + argIndex + "}";
            });

            if (arguments.Count == 0)
            {
                return new CodePrimitiveExpression(text);
            }

            var formatArguments = new List<CodeExpression> { new CodePrimitiveExpression(formatText) };
            formatArguments.AddRange(arguments.Select(id => new CodeVariableReferenceExpression(id))
                .Cast<CodeExpression>());

            return new CodeMethodInvokeExpression(
                new CodeTypeReferenceExpression(typeof(string)),
                "Format",
                formatArguments.ToArray());
        }

        private static CodeExpression GetStringArrayExpression(
            IEnumerable<string> items,
            ParameterSubstitution paramToIdentifier)
        {
            return new CodeArrayCreateExpression(
                typeof(string[]),
                items.Select(item => GetSubstitutedString(item, paramToIdentifier)).ToArray());
        }

        private static CodeExpression GetStringArrayExpression(IEnumerable<Tag> tags)
        {
            var enumerable = tags as Tag[] ?? tags.ToArray();
            if (!enumerable.Any())
            {
                return new CodeCastExpression(typeof(string[]), new CodePrimitiveExpression(null));
            }

            return new CodeArrayCreateExpression(
                typeof(string[]),
                enumerable.Select(tag => new CodePrimitiveExpression(tag.GetNameWithoutAt())).Cast<CodeExpression>()
                    .ToArray());
        }

        private static void SetupScenarioInitializeMethod(TestClassGenerationContext generationContext)
        {
            CodeMemberMethod scenarioInitializeMethod = generationContext.ScenarioInitializeMethod;

            scenarioInitializeMethod.Attributes = MemberAttributes.Public;
            scenarioInitializeMethod.Name = SCENARIOINITIALIZENAME;
            scenarioInitializeMethod.Parameters.Add(
                new CodeParameterDeclarationExpression(typeof(ScenarioInfo), "scenarioInfo"));

            var testRunnerField = GetTestRunnerExpression();
            scenarioInitializeMethod.Statements.Add(
                new CodeMethodInvokeExpression(
                    testRunnerField,
                    "OnScenarioStart",
                    new CodeVariableReferenceExpression("scenarioInfo")));
        }

        private static void SetupScenarioCleanupMethod(TestClassGenerationContext generationContext)
        {
            CodeMemberMethod scenarioCleanupMethod = generationContext.ScenarioCleanupMethod;

            scenarioCleanupMethod.Attributes = MemberAttributes.Public;
            scenarioCleanupMethod.Name = SCENARIOCLEANUPNAME;

            // call collect errors
            var testRunnerField = GetTestRunnerExpression();
            scenarioCleanupMethod.Statements.Add(
                new CodeMethodInvokeExpression(
                    testRunnerField,
                    "CollectScenarioErrors"));
        }

        private static CodeMemberMethod GenerateRetryStatementAndUnwrap(
            TestClassGenerationContext generationContext,
            CodeMemberMethod testMethod,
            int retryCount,
            string retryExceptExceptionName)
        {
            var method = CreateMethod(generationContext.TestClass);

            method.Name = testMethod.Name + "Internal";

            foreach (CodeParameterDeclarationExpression parameter in testMethod.Parameters)
            {
                method.Parameters.Add(new CodeParameterDeclarationExpression(parameter.Type, parameter.Name));
            }

            testMethod.Statements.Add(
                new CodeVariableDeclarationStatement(
                    typeof(Exception),
                    "lastException",
                    new CodePrimitiveExpression(null)));

            var codeCatchClauses = new List<CodeCatchClause>();

            if (!string.IsNullOrEmpty(retryExceptExceptionName))
            {
                codeCatchClauses.Add(new CodeCatchClause(
                    "exc",
                    new CodeTypeReference(new CodeTypeParameter(retryExceptExceptionName)),
                    new CodeThrowExceptionStatement()));
            }

            codeCatchClauses.Add(new CodeCatchClause(
                "exc",
                new CodeTypeReference(typeof(Exception)),
                new CodeAssignStatement(
                    new CodeVariableReferenceExpression("lastException"),
                    new CodeVariableReferenceExpression("exc"))));

            testMethod.Statements.Add(
                new CodeIterationStatement(
                    initStatement: new CodeVariableDeclarationStatement(
                        typeof(int),
                        "i",
                        new CodePrimitiveExpression(0)),
                    testExpression: new CodeBinaryOperatorExpression(
                        new CodeVariableReferenceExpression("i"),
                        CodeBinaryOperatorType.LessThanOrEqual,
                        new CodePrimitiveExpression(retryCount)),
                    incrementStatement: new CodeAssignStatement(
                        new CodeVariableReferenceExpression("i"),
                        new CodeBinaryOperatorExpression(
                            new CodeVariableReferenceExpression("i"),
                            CodeBinaryOperatorType.Add,
                            new CodePrimitiveExpression(1))),
                    statements: new CodeStatement[]
                    {
                        new CodeTryCatchFinallyStatement(
                            tryStatements: new CodeStatement[]
                            {
                                new CodeExpressionStatement(new CodeMethodInvokeExpression(
                                    new CodeThisReferenceExpression(),
                                    method.Name,
                                    method.Parameters
                                        .Cast<CodeParameterDeclarationExpression>()
                                        .Select(_ => new CodeVariableReferenceExpression(_.Name))
                                        .Cast<CodeExpression>()
                                        .ToArray())),
                                new CodeMethodReturnStatement(),
                            },
                            catchClauses: codeCatchClauses.ToArray()),
                        new CodeConditionStatement(
                            new CodeBinaryOperatorExpression(
                                new CodeBinaryOperatorExpression(
                                    new CodeVariableReferenceExpression("i"),
                                    CodeBinaryOperatorType.Add,
                                    new CodePrimitiveExpression(1)),
                                CodeBinaryOperatorType.LessThanOrEqual,
                                new CodePrimitiveExpression(retryCount)),
                            new CodeExpressionStatement(
                                new CodeMethodInvokeExpression(
                                    GetTestRunnerExpression(),
                                    "OnScenarioEnd"))),
                    }));
            testMethod.Statements.Add(new CodeConditionStatement(
                condition: new CodeBinaryOperatorExpression(
                    new CodeVariableReferenceExpression("lastException"),
                    CodeBinaryOperatorType.IdentityInequality,
                    new CodePrimitiveExpression(null)),
                trueStatements: new CodeThrowExceptionStatement(new CodeVariableReferenceExpression("lastException"))));

            return method;
        }

        private static CodeExpression GetDocStringArgExpression(DocString docString, ParameterSubstitution paramToIdentifier)
        {
            return GetSubstitutedString(docString?.Content, paramToIdentifier);
        }

        private void SetupTestClass(TestClassGenerationContext generationContext)
        {
            generationContext.TestClass.IsPartial = true;
            generationContext.TestClass.TypeAttributes |= TypeAttributes.Public;

            this.AddLinePragmaInitial(generationContext.TestClass, generationContext.Document.SourceFilePath);

            this.testGeneratorProvider.SetTestClass(
                generationContext,
                generationContext.Feature.Name,
                generationContext.Feature.Description);

            this.decoratorRegistry.DecorateTestClass(generationContext, out List<string> featureCategories);

            if (featureCategories.Any())
            {
                this.testGeneratorProvider.SetTestClassCategories(generationContext, featureCategories);
            }
        }

        private TestClassGenerationContext CreateTestClassStructure(
            CodeNamespace codeNamespace,
            string testClassName,
            SpecFlowDocument document)
        {
            var testClass = this.codeDomHelper.CreateGeneratedTypeDeclaration(testClassName);
            codeNamespace.Types.Add(testClass);

            return new TestClassGenerationContext(
                this.testGeneratorProvider,
                document,
                codeNamespace,
                testClass,
                DeclareTestRunnerMember(testClass),
                CreateMethod(testClass),
                CreateMethod(testClass),
                CreateMethod(testClass),
                CreateMethod(testClass),
                CreateMethod(testClass),
                CreateMethod(testClass),
                CreateMethod(testClass),
                HasFeatureBackground(document.SpecFlowFeature) ? CreateMethod(testClass) : null,
                generateRowTests: this.testGeneratorProvider.GetTraits().HasFlag(UnitTestGeneratorTraits.RowTests) && this.specFlowConfiguration.AllowRowTests);
        }

        private void SetupTestClassInitializeMethod(TestClassGenerationContext generationContext)
        {
            var testClassInitializeMethod = generationContext.TestClassInitializeMethod;

            testClassInitializeMethod.Attributes = MemberAttributes.Public;
            testClassInitializeMethod.Name = TESTCLASSINITIALIZENAME;

            this.testGeneratorProvider.SetTestClassInitializeMethod(generationContext);

            var testRunnerField = GetTestRunnerExpression();

            var testRunnerParameters =
                this.testGeneratorProvider.GetTraits().HasFlag(UnitTestGeneratorTraits.ParallelExecution)
                    ? Array.Empty<CodeExpression>()
                    : new[] { new CodePrimitiveExpression(null), new CodePrimitiveExpression(0) };

            testClassInitializeMethod.Statements.Add(
                new CodeAssignStatement(
                    testRunnerField,
                    new CodeMethodInvokeExpression(
                        new CodeTypeReferenceExpression(
                            typeof(TestRunnerManager)),
                        "GetTestRunner",
                        testRunnerParameters)));

            _ = testClassInitializeMethod.Statements.Add(
                new CodeVariableDeclarationStatement(
                    typeof(FeatureInfo),
                    "featureInfo",
                    new CodeObjectCreateExpression(
                        typeof(FeatureInfo),
                        new CodeObjectCreateExpression(
                            typeof(CultureInfo),
                            new CodePrimitiveExpression(generationContext.Feature.Language)),
                        new CodePrimitiveExpression(generationContext.Feature.Name),
                        new CodePrimitiveExpression(generationContext.Feature.Description),
                        new CodeFieldReferenceExpression(
                            new CodeTypeReferenceExpression("ProgrammingLanguage"),
                            this.codeDomHelper.TargetLanguage.ToString()),
                        GetStringArrayExpression(generationContext.Feature.Tags))));

            testClassInitializeMethod.Statements.Add(
                new CodeMethodInvokeExpression(
                    testRunnerField,
                    "OnFeatureStart",
                    new CodeVariableReferenceExpression("featureInfo")));
        }

        private void SetupTestClassCleanupMethod(TestClassGenerationContext generationContext)
        {
            CodeMemberMethod testClassCleanupMethod = generationContext.TestClassCleanupMethod;

            testClassCleanupMethod.Attributes = MemberAttributes.Public;
            testClassCleanupMethod.Name = TESTCLASSCLEANUPNAME;

            this.testGeneratorProvider.SetTestClassCleanupMethod(generationContext);

            var testRunnerField = GetTestRunnerExpression();
            testClassCleanupMethod.Statements.Add(
                new CodeMethodInvokeExpression(
                    testRunnerField,
                    "OnFeatureEnd"));
            testClassCleanupMethod.Statements.Add(
                new CodeAssignStatement(
                    testRunnerField,
                    new CodePrimitiveExpression(null)));
        }

        private void SetupTestInitializeMethod(TestClassGenerationContext generationContext)
        {
            CodeMemberMethod testInitializeMethod = generationContext.TestInitializeMethod;

            testInitializeMethod.Attributes = MemberAttributes.Public;
            testInitializeMethod.Name = TESTINITIALIZENAME;

            this.testGeneratorProvider.SetTestInitializeMethod(generationContext);
        }

        private void SetupTestCleanupMethod(TestClassGenerationContext generationContext)
        {
            CodeMemberMethod testCleanupMethod = generationContext.TestCleanupMethod;

            testCleanupMethod.Attributes = MemberAttributes.Public;
            testCleanupMethod.Name = TESTCLEANUPNAME;

            this.testGeneratorProvider.SetTestCleanupMethod(generationContext);

            var testRunnerField = GetTestRunnerExpression();
            testCleanupMethod.Statements.Add(
                new CodeMethodInvokeExpression(
                    testRunnerField,
                    "OnScenarioEnd"));
        }

        private void SetupFeatureBackground(TestClassGenerationContext generationContext)
        {
            if (!HasFeatureBackground(generationContext.Feature))
            {
                return;
            }

            var background = generationContext.Feature.Background;

            CodeMemberMethod backgroundMethod = generationContext.FeatureBackgroundMethod;

            backgroundMethod.Attributes = MemberAttributes.Public;
            backgroundMethod.Name = BACKGROUNDNAME;

            foreach (var step in background.Steps)
            {
                this.GenerateStep(backgroundMethod, step, null);
            }
        }

        private CodeMemberMethod CreateTestMethod(
            TestClassGenerationContext generationContext,
            Scenario scenario,
            IEnumerable<Tag> additionalTags,
            string variantName = null,
            string exampleSetIdentifier = null)
        {
            CodeMemberMethod testMethod = CreateMethod(generationContext.TestClass);

            this.SetupTestMethod(generationContext, testMethod, scenario, additionalTags, variantName, exampleSetIdentifier);

            return testMethod;
        }

        private void GenerateTest(TestClassGenerationContext generationContext, Scenario scenario)
        {
            CodeMemberMethod testMethod = this.CreateTestMethod(generationContext, scenario, null);

            if (this.GetTagValue(generationContext, scenario, TagsRepository.RetryTag, int.TryParse, out int retryValue))
            {
                this.GetTagValue(generationContext, scenario, TagsRepository.RetryExceptTag, out string retryExceptExceptionName);

                testMethod = GenerateRetryStatementAndUnwrap(
                    generationContext,
                    testMethod,
                    retryValue,
                    retryExceptExceptionName);
            }

            this.GenerateTestBody(generationContext, scenario, testMethod);
        }

        private void GetTagValue(
            TestClassGenerationContext generationContext,
            Scenario scenario,
            string retryTag,
            out string value)
        {
            this.GetTagValue(
                generationContext,
                scenario,
                retryTag,
                delegate(string v, out string p)
            {
                p = v;
                return v != null;
            },
                out value);
        }

        private bool GetTagValue<T>(
            TestClassGenerationContext generationContext,
            Scenario scenario,
            string retryTag,
            TryParseDelegate<T> parser,
            out T value)
        {
            value = default;

            var tagNames = scenario.Tags?.Select(_ => _.GetNameWithoutAt()) ?? Array.Empty<string>();
            tagNames = tagNames.ToList();
            return (this.tagFilterMatcher.GetTagValue(retryTag, tagNames, out string retryCountValue)
                   && parser(retryCountValue, out value)) ||
                   (this.tagFilterMatcher.GetTagValue(retryTag, generationContext.Document, out retryCountValue)
                   && parser(retryCountValue, out value));
        }

        private void GenerateTestBody(
            TestClassGenerationContext generationContext,
            Scenario scenario,
            CodeMemberMethod testMethod,
            CodeExpression additionalTagsExpression = null,
            ParameterSubstitution paramToIdentifier = null)
        {
            //// call test setup
            //// ScenarioInfo scenarioInfo = new ScenarioInfo("xxxx", tags...);
            CodeExpression tagsExpression;

            if (additionalTagsExpression == null)
            {
                tagsExpression = GetStringArrayExpression(scenario.GetTags());
            }
            else if (!((StepsContainer)scenario).HasTags())
            {
                tagsExpression = additionalTagsExpression;
            }
            else
            {
                testMethod.Statements.Add(
                    new CodeVariableDeclarationStatement(
                        typeof(string[]),
                        "__tags",
                        GetStringArrayExpression(scenario.GetTags())));
                tagsExpression = new CodeVariableReferenceExpression("__tags");
                testMethod.Statements.Add(
                    new CodeConditionStatement(
                        new CodeBinaryOperatorExpression(
                            additionalTagsExpression,
                            CodeBinaryOperatorType.IdentityInequality,
                            new CodePrimitiveExpression(null)),
                        new CodeAssignStatement(
                            tagsExpression,
                            new CodeMethodInvokeExpression(
                                new CodeTypeReferenceExpression(typeof(Enumerable)),
                                "ToArray",
                                new CodeMethodInvokeExpression(
                                    new CodeTypeReferenceExpression(typeof(Enumerable)),
                                    "Concat",
                                    tagsExpression,
                                    additionalTagsExpression)))));
            }

            testMethod.Statements.Add(
                new CodeVariableDeclarationStatement(
                    typeof(ScenarioInfo),
                    "scenarioInfo",
                    new CodeObjectCreateExpression(
                        typeof(ScenarioInfo),
                        new CodePrimitiveExpression(scenario.Name),
                        tagsExpression)));

            testMethod.Statements.Add(
                new CodeMethodInvokeExpression(
                    new CodeThisReferenceExpression(),
                    generationContext.ScenarioInitializeMethod.Name,
                    new CodeVariableReferenceExpression("scenarioInfo")));

            if (HasFeatureBackground(generationContext.Feature))
            {
                testMethod.Statements.Add(
                    new CodeMethodInvokeExpression(
                        new CodeThisReferenceExpression(),
                        generationContext.FeatureBackgroundMethod.Name));
            }

            foreach (var scenarioStep in scenario.Steps)
            {
                this.GenerateStep(testMethod, scenarioStep, paramToIdentifier);
            }

            // call scenario cleanup
            testMethod.Statements.Add(
                new CodeMethodInvokeExpression(
                    new CodeThisReferenceExpression(),
                    generationContext.ScenarioCleanupMethod.Name));
        }

        private void SetupTestMethod(
            TestClassGenerationContext generationContext,
            CodeMemberMethod testMethod,
            Scenario scenarioDefinition,
            IEnumerable<Tag> additionalTags,
            string variantName,
            string exampleSetIdentifier,
            bool rowTest = false)
        {
            testMethod.Attributes = MemberAttributes.Public;
            testMethod.Name = GetTestMethodName(scenarioDefinition, variantName, exampleSetIdentifier);

            var friendlyTestName = scenarioDefinition.Name;

            if (variantName != null)
            {
                friendlyTestName = string.Format("{0}: {1}", scenarioDefinition.Name, variantName);
            }

            if (rowTest)
            {
                this.testGeneratorProvider.SetRowTest(generationContext, testMethod, friendlyTestName);
            }
            else
            {
                this.testGeneratorProvider.SetTestMethod(generationContext, testMethod, friendlyTestName);
            }

            this.decoratorRegistry.DecorateTestMethod(
                generationContext,
                testMethod,
                ConcatTags(scenarioDefinition.GetTags(), additionalTags),
                out List<string> scenarioCategories);

            if (scenarioCategories.Any())
            {
                this.testGeneratorProvider.SetTestMethodCategories(generationContext, testMethod, scenarioCategories);
            }
        }

        private void GenerateStep(
            CodeMemberMethod testMethod,
            Step gherkinStep,
            ParameterSubstitution paramToIdentifier)
        {
            var testRunnerField = GetTestRunnerExpression();
            var scenarioStep = AsSpecFlowStep(gherkinStep);

            //// testRunner.Given("something");
            var arguments =
                new List<CodeExpression> { GetSubstitutedString(scenarioStep.Text, paramToIdentifier) };

            arguments.Add(
                GetDocStringArgExpression(scenarioStep.Argument as DocString, paramToIdentifier));
            arguments.Add(
                this.GetTableArgExpression(scenarioStep.Argument as DataTable, testMethod.Statements, paramToIdentifier));
            arguments.Add(new CodePrimitiveExpression(scenarioStep.Keyword));
            testMethod.Statements.Add(
                new CodeMethodInvokeExpression(
                    testRunnerField,
                    scenarioStep.StepKeyword.ToString(),
                    arguments.ToArray()));
        }

        private CodeExpression GetTableArgExpression(
            DataTable tableArg,
            CodeStatementCollection statements,
            ParameterSubstitution paramToIdentifier)
        {
            if (tableArg == null)
            {
                return new CodeCastExpression(typeof(Table), new CodePrimitiveExpression(null));
            }

            this.tableCounter++;

            var header = tableArg.Rows.First();
            var body = tableArg.Rows.Skip(1).ToArray();

            var tableVar = new CodeVariableReferenceExpression("table" + this.tableCounter);
            statements.Add(
                new CodeVariableDeclarationStatement(
                    typeof(Table),
                    tableVar.VariableName,
                    new CodeObjectCreateExpression(
                        typeof(Table),
                        GetStringArrayExpression(header.Cells.Select(c => c.Value), paramToIdentifier))));

            foreach (var row in body)
            {
                statements.Add(
                    new CodeMethodInvokeExpression(
                        tableVar,
                        "AddRow",
                        GetStringArrayExpression(row.Cells.Select(c => c.Value), paramToIdentifier)));
            }

            return tableVar;
        }

        private void AddLinePragmaInitial(CodeTypeDeclaration testType, string sourceFile)
        {
            if (this.specFlowConfiguration.AllowDebugGeneratedFiles)
            {
                return;
            }

            this.codeDomHelper.BindTypeToSourceFile(testType, Path.GetFileName(sourceFile));
        }

        private class ParameterSubstitution : List<KeyValuePair<string, string>>
        {
            public bool TryGetIdentifier(string param, out string id)
            {
                param = param.Trim();
                foreach (var pair in this)
                {
                    if (pair.Key.Equals(param))
                    {
                        id = pair.Value;
                        return true;
                    }
                }

                id = null;
                return false;
            }
        }
    }
}
