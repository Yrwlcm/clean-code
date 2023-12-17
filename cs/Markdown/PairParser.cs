﻿using System.Text;

namespace Markdown;

public class PairParser : IPairParser
{
    private IEnumerable<Tag> tags;

    public PairParser(IEnumerable<Tag> tags)
    {
        this.tags = tags;
    }

    public IEnumerable<TagPair> ParseTagPairs(string markdownText)
    {
        var allTags = FindTags(markdownText);
        var noEscapedTags = RemoveEscapedTags(allTags);
        var pairedTags = CreatePairs(noEscapedTags);
        return pairedTags;
    }

    public List<MarkdownTagInfo> FindTags(string markdownText)
    {
        var tagsIndexes = new List<MarkdownTagInfo>();
        var seenWhitespace = false;
        for (int i = 0; i < markdownText.Length; i++)
        {
            var tag = FindTag(markdownText, i);
            seenWhitespace = char.IsWhiteSpace(markdownText[i]) ? true : seenWhitespace;
            if (tag == null) continue;

            SetAdditionalInfo(tag, markdownText, seenWhitespace);
            seenWhitespace = false;
            tagsIndexes.Add(tag);
            i = tag.EndIndex;
        }

        return tagsIndexes;
    }

    private void SetAdditionalInfo(MarkdownTagInfo tagInfo, string markdownText, bool seenWhitespace)
    {
        tagInfo.WhitespacesBefore = seenWhitespace;
        var leftPos = tagInfo.StartIndex - 1;
        var rightPos = tagInfo.EndIndex + 1;
        
        var leftNumeric = false;
        var rightNumeric = false;
        
        var leftWhitespace = true;
        var rightWhitespace = true;

        var leftLetter = false;
        var rightLetter = false;
        if (leftPos > 0)
        {
            leftNumeric = char.IsNumber(markdownText[leftPos]);
            leftWhitespace = char.IsWhiteSpace(markdownText[leftPos]);
            leftLetter = char.IsLetter(markdownText[leftPos]) || (char.IsPunctuation(markdownText[leftPos]) && markdownText[leftPos] != '\\');
        }

        if (rightPos < markdownText.Length - 1)
        {
            rightNumeric = char.IsNumber(markdownText[rightPos]);
            rightWhitespace = char.IsWhiteSpace(markdownText[rightPos]);
            rightLetter = char.IsLetter(markdownText[rightPos]);
        }

        if (leftNumeric && rightNumeric) tagInfo.InNumber = true;
        if (leftLetter && rightLetter) tagInfo.InWord = true;
        if ((leftWhitespace || !leftLetter && !leftNumeric) && rightLetter) tagInfo.IsOpening = true;
        if ((rightWhitespace || !rightLetter && !rightNumeric) && leftLetter) tagInfo.IsClosing = true;
    }

    public List<MarkdownTagInfo> RemoveEscapedTags(List<MarkdownTagInfo> tagsList)
    {
        var newTags = new List<MarkdownTagInfo>(tagsList.Count);
        for (int i = 0; i < tagsList.Count; i++)
        {
            var currentTag = tagsList[i];
            if (currentTag.Tag != Tags.Escape)
            {
                newTags.Add(currentTag);
                continue;
            };
            i++;
            if (i > tagsList.Count - 1) 
                break;

            var nextTag = tagsList[i];

            if (nextTag.StartIndex != currentTag.StartIndex + 1) 
                newTags.Add(nextTag);
        }
        return newTags;
    }

    public List<TagPair> CreatePairs(List<MarkdownTagInfo> tagsList)
    {
        var tagPairs = new List<TagPair>(tagsList.Count);
        var tagsStack = new Stack<MarkdownTagInfo>();
        for (int i = 0; i < tagsList.Count; i++)
        {
            var currentTagInfo = tagsList[i];
            var tagsInStack = tagsStack.TryPeek(out var lastTagInfo);
            if (tagsInStack && lastTagInfo.Tag == currentTagInfo.Tag)
            {
                if (!currentTagInfo.IsClosing && !currentTagInfo.InWord) 
                    continue;

                if (lastTagInfo.InWord && currentTagInfo.WhitespacesBefore)
                {
                    tagsStack.Pop();
                    continue;
                }
                tagsStack.Pop();
                if (tagsStack.Count != 0 && tagsStack.Peek().Tag == Tags.Italic && currentTagInfo.Tag == Tags.Bold)
                    continue;
                tagPairs.Add(new TagPair(lastTagInfo, currentTagInfo));
            }
            else if (currentTagInfo.Tag == Tags.LineFeed)
            {
                CloseAllTags(lastTagInfo, currentTagInfo, tagPairs, tagsStack, tagsInStack);
            }
            else if (tagsInStack && currentTagInfo.IsClosing)
            {
                tagsStack.Pop();
            }
            else
            {
                if (!currentTagInfo.IsOpening && !currentTagInfo.InWord)
                    continue;
                tagsStack.Push(currentTagInfo);
            }
        }
        return tagPairs;
    }

    private static void CloseAllTags(
        MarkdownTagInfo? lastTagInfo, 
        MarkdownTagInfo currentTagInfo, 
        List<TagPair> tagPairs,
        Stack<MarkdownTagInfo> tagsStack,
        bool tagsInStack)
    {
        while (tagsInStack)
        {
            if (lastTagInfo.Tag.MarkdownClosing != null)
            {
                tagsStack.Pop();
                tagsInStack = tagsStack.TryPeek(out lastTagInfo);
                continue;
            }

            var closingPairTag = new MarkdownTagInfo(lastTagInfo.Tag);
            closingPairTag.StartIndex = currentTagInfo.StartIndex;
            closingPairTag.EndIndex = currentTagInfo.StartIndex;

            tagPairs.Add(new TagPair(lastTagInfo, closingPairTag));
            tagsStack.Pop();
            tagsInStack = tagsStack.TryPeek(out lastTagInfo);
        }
    }

    private MarkdownTagInfo FindTag(string markdownText, int i)
    {
        var substringIndex = i;
        var substring = new StringBuilder();
        substring.Append(markdownText[substringIndex]);

        var markdownSubstring = substring.ToString();
        Tag? resultTag = null;
        do
        {
            var currentTag = FindFistTagBySubstring(markdownSubstring);
            if (currentTag == null) break;

            resultTag = currentTag;
            substringIndex++;
            if (substringIndex > markdownText.Length - 1) break;

            substring.Append(markdownText[substringIndex]);
            markdownSubstring = substring.ToString();
        } while (true);

        var tagInfo = new MarkdownTagInfo(resultTag);

        tagInfo.StartIndex = i;
        tagInfo.EndIndex = substringIndex-1;

        return resultTag != null ? tagInfo : null;
    }

    private Tag? FindFistTagBySubstring(string substring)
    {
        var tagByOpening = FindFirstTagByOpening(substring);
        return tagByOpening ?? FindFirstTagByClosing(substring);
    }

    private Tag? FindFirstTagByOpening(string markdownOpening)
    {
        return tags.FirstOrDefault(tag => tag.MarkdownOpening != null && tag.MarkdownOpening.StartsWith(markdownOpening));
    }

    private Tag? FindFirstTagByClosing(string markdownClosing)
    {
        return tags.FirstOrDefault(tag => tag.MarkdownClosing != null && tag.MarkdownClosing.StartsWith(markdownClosing));
    }
}