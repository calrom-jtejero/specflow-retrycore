// -----------------------------------------------------------------------
// <copyright file="RetryUnitTestFeatureGeneratorProvider.cs" company="Calrom Ltd.">
// Under MIT license
// </copyright>
// -----------------------------------------------------------------------

namespace CalromSpecFlowRetryCore
{
    using TechTalk.SpecFlow.Generator.UnitTestConverter;
    using TechTalk.SpecFlow.Parser;

    public class RetryUnitTestFeatureGeneratorProvider : IFeatureGeneratorProvider
    {
        private readonly RetryUnitTestFeatureGenerator unitTestFeatureGenerator;

        /// <summary>
        /// Initialises a new instance of the <see cref="RetryUnitTestFeatureGeneratorProvider"/> class.
        /// </summary>
        /// <param name="unitTestFeatureGenerator">give me.</param>
        public RetryUnitTestFeatureGeneratorProvider(RetryUnitTestFeatureGenerator unitTestFeatureGenerator)
        {
            this.unitTestFeatureGenerator = unitTestFeatureGenerator;
        }

        public int Priority => PriorityValues.Normal;

        public bool CanGenerate(SpecFlowDocument document)
        {
            return true;
        }

        public IFeatureGenerator CreateGenerator(SpecFlowDocument document)
        {
            return this.unitTestFeatureGenerator;
        }
    }
}
