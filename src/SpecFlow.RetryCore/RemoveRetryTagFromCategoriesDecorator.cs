// -----------------------------------------------------------------------
// <copyright file="RemoveRetryTagFromCategoriesDecorator.cs" company="Calrom Ltd.">
// Under MIT license
// </copyright>
// -----------------------------------------------------------------------

namespace CalromSpecFlowRetryCore
{
    using System.CodeDom;
    using TechTalk.SpecFlow.Generator;
    using TechTalk.SpecFlow.Generator.UnitTestConverter;

    public class RemoveRetryTagFromCategoriesDecorator : ITestClassTagDecorator, ITestMethodTagDecorator
    {
        private readonly ITagFilterMatcher tagFilterMatcher;

        /// <summary>
        /// Initialises a new instance of the <see cref="RemoveRetryTagFromCategoriesDecorator"/> class.
        /// </summary>
        /// <param name="tagFilterMatcher">hello dolly.</param>
        public RemoveRetryTagFromCategoriesDecorator(ITagFilterMatcher tagFilterMatcher)
        {
            this.tagFilterMatcher = tagFilterMatcher;
        }

        int ITestMethodTagDecorator.Priority => PriorityValues.High;

        bool ITestMethodTagDecorator.RemoveProcessedTags => true;

        bool ITestMethodTagDecorator.ApplyOtherDecoratorsForProcessedTags => true;

        int ITestClassTagDecorator.Priority => PriorityValues.High;

        bool ITestClassTagDecorator.RemoveProcessedTags => true;

        bool ITestClassTagDecorator.ApplyOtherDecoratorsForProcessedTags => true;

        public bool CanDecorateFrom(string tagName, TestClassGenerationContext generationContext)
        {
            return this.CanDecorateFrom(tagName);
        }

        public bool CanDecorateFrom(string tagName, TestClassGenerationContext generationContext, CodeMemberMethod testMethod)
        {
            return this.CanDecorateFrom(tagName);
        }

        public void DecorateFrom(string tagName, TestClassGenerationContext generationContext)
        {
            // Method intentionally left empty.
        }

        public void DecorateFrom(string tagName, TestClassGenerationContext generationContext, CodeMemberMethod testMethod)
        {
            // Method intentionally left empty.
        }

        private bool CanDecorateFrom(string tagName)
        {
            var tagNames = new[] { tagName };
            return this.tagFilterMatcher.MatchPrefix(TagsRepository.RetryTag, tagNames) ||
                this.tagFilterMatcher.MatchPrefix(TagsRepository.RetryExceptTag, tagNames);
        }
    }
}
