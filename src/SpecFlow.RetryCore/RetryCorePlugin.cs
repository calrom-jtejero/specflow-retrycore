// -----------------------------------------------------------------------
// <copyright file="RetryCorePlugin.cs" company="Calrom Ltd.">
// Under MIT license
// </copyright>
// -----------------------------------------------------------------------

using CalromSpecFlowRetryCore;
using TechTalk.SpecFlow.Generator.Plugins;
using TechTalk.SpecFlow.Generator.UnitTestConverter;
using TechTalk.SpecFlow.Infrastructure;
using TechTalk.SpecFlow.UnitTestProvider;

[assembly: GeneratorPlugin(typeof(RetryCorePlugin))]

namespace CalromSpecFlowRetryCore
{
    public class RetryCorePlugin : IGeneratorPlugin
    {
        public void Initialize(
            GeneratorPluginEvents generatorPluginEvents,
            GeneratorPluginParameters generatorPluginParameters,
            UnitTestProviderConfiguration unitTestProviderConfiguration)
        {
            generatorPluginEvents.RegisterDependencies += (sender, args) =>
                {
                    args.ObjectContainer.RegisterTypeAs<RetryUnitTestFeatureGenerator, IFeatureGenerator>();
                    args.ObjectContainer.RegisterTypeAs<RetryUnitTestFeatureGeneratorProvider, IFeatureGeneratorProvider>("retry");
                    args.ObjectContainer.RegisterTypeAs<RemoveRetryTagFromCategoriesDecorator, ITestClassTagDecorator>("retry");
                    args.ObjectContainer.RegisterTypeAs<RemoveRetryTagFromCategoriesDecorator, ITestMethodTagDecorator>("retry");
                };
        }
    }
}
