


using Textamina.Markdig.Helpers;
using Textamina.Markdig.Parsing;

namespace Textamina.Markdig.Syntax
{
    public class ListBlock : ContainerBlock
    {
        public static readonly BlockParser Parser = new ParserInternal();

        public bool IsOrdered { get; set; }

        public char BulletChar { get; set; }

        public int OrderedStart { get; set; }

        public char OrderedDelimiter { get; set; }

        public bool IsLoose { get; set; }

        private int consecutiveBlankLines;

        private class ParserInternal : BlockParser
        {
            public override MatchLineResult Match(MatchLineState state)
            {
                var liner = state.Line;

                // 5.2 List items 
                // TODO: Check with specs, it is not clear that list marker or bullet marker must be followed by at least 1 space

                int preIndent = 0;
                for (int i = liner.Start - 1; i >= 0; i--)
                {
                    if (liner[i].IsSpaceOrTab())
                    {
                        preIndent++;
                    }
                    else
                    {
                        break;
                    }
                }

                var saveLiner = liner.Save();

                // If we have already a ListItemBlock, we are going to try to append to it
                var listItem = state.Block as ListItemBlock;
                if (listItem != null)
                {
                    var list = (ListBlock) listItem.Parent;

                    // Allow all blanks lines if the last block is a fenced code block
                    // Allow 1 blank line inside a list
                    // If > 1 blank line, terminate this list
                    var isBlankLine = liner.IsBlankLine();
                    //if (isBlankLine && !(state.LastBlock is FencedCodeBlock)) // TODO: Handle this case
                    if (isBlankLine)
                    {
                        if (!(state.LastBlock is FencedCodeBlock))
                        {
                            list.consecutiveBlankLines++;

                            if (list.consecutiveBlankLines == 1 && list.Children.Count == 1)
                            {
                                listItem.IsFollowedByBlankLine = true;
                            }
                        }

                        if (list.consecutiveBlankLines > 1)
                        {
                            // TODO: Close all lists and not only this one
                            return MatchLineResult.LastDiscard;
                        }

                        return MatchLineResult.Continue;
                    }

                    var c = liner.Current;
                    var startPosition = liner.Column;

                    // List Item starting with a blank line (-1)
                    if (listItem.NumberOfSpaces < 0)
                    {
                        int expectedCount = -listItem.NumberOfSpaces;
                        int countSpaces = 0;
                        var saved = new StringLine.State();
                        while (c.IsSpaceOrTab())
                        {
                            c = liner.NextChar();
                            countSpaces = preIndent + liner.Column - startPosition;
                            if (countSpaces == expectedCount)
                            {
                                saved = liner.Save();
                            }
                            else if (countSpaces >= 4)
                            {
                                liner.Restore(ref saved);
                                countSpaces = expectedCount;
                                break;
                            }
                        }

                        if (countSpaces == expectedCount)
                        {
                            listItem.NumberOfSpaces = countSpaces;
                            list.consecutiveBlankLines = 0;
                            return MatchLineResult.Continue;
                        }
                    }
                    else
                    {
                        while (c.IsSpaceOrTab())
                        {
                            c = liner.NextChar();
                            var countSpaces = preIndent + liner.Column - startPosition;
                            if (countSpaces >= listItem.NumberOfSpaces)
                            {
                                list.consecutiveBlankLines = 0;
                                return MatchLineResult.Continue;
                            }
                        }
                    }
                    liner.Restore(ref saveLiner);
                }

                return TryParseListItem(ref state, preIndent);
            }

            private MatchLineResult TryParseListItem(ref MatchLineState state, int preIndent)
            {
                var liner = state.Line;

                var preStartPosition = liner.Start;
                liner.SkipLeadingSpaces3();
                preIndent = preIndent + liner.Start - preStartPosition;
                var c = liner.Current;

                var isOrdered = false;
                var bulletChar = (char) 0;
                int orderedStart = 0;
                var orderedDelimiter = (char) 0;

                if (c.IsBulletListMarker())
                {
                    bulletChar = c;
                    preIndent++;
                }
                else if (c.IsDigit())
                {
                    int countDigit = 0;
                    while (c.IsDigit())
                    {
                        orderedStart = orderedStart*10 + c - '0';
                        c = liner.NextChar();
                        preIndent++;
                        countDigit++;
                    }

                    // Note that ordered list start numbers must be nine digits or less:
                    if (countDigit > 9)
                    {
                        return MatchLineResult.None;
                    }

                    // We don't have an ordered list
                    if (c != '.' && c != ')')
                    {
                        return MatchLineResult.None;
                    }
                    preIndent++;
                    isOrdered = true;
                    orderedDelimiter = c;
                }
                else
                {
                    return MatchLineResult.None;
                }

                // Skip Bullet or '.'
                liner.NextChar();

                // Item starting with a blank line
                int numberOfSpaces;
                if (liner.IsBlankLine())
                {
                    // Use a negative number to store the number of expected chars
                    numberOfSpaces = -(preIndent + 1);
                }
                else
                {
                    var startPosition = -1;
                    int countSpaceAfterBullet = 0;
                    var saved = new StringLine.State();
                    for (int i = 0; i <= 4; i++)
                    {
                        c = liner.Current;
                        if (!c.IsSpaceOrTab())
                        {
                            break;
                        }
                        if (i == 0)
                        {
                            startPosition = liner.Column;
                        }

                        var endPosition = liner.Column;
                        countSpaceAfterBullet = endPosition - startPosition;

                        if (countSpaceAfterBullet == 1)
                        {
                            saved = liner.Save();
                        }
                        else if (countSpaceAfterBullet >= 4)
                        {
                            liner.SpaceHeaderCount = countSpaceAfterBullet - 4;
                            countSpaceAfterBullet = 0;
                            liner.Restore(ref saved);
                            break;
                        }
                        liner.NextChar();
                    }

                    // If we haven't matched any spaces, early exit
                    if (startPosition < 0)
                    {
                        return MatchLineResult.None;
                    }
                    // Number of spaces required for the following content to be part of this list item
                    numberOfSpaces = preIndent + countSpaceAfterBullet + 1;
                }

                var listItem = new ListItemBlock()
                {
                    NumberOfSpaces = numberOfSpaces
                };

                var parentList = (state.Block as ListItemBlock)?.Parent as ListBlock;

                // Reset the list if it is a new list or a new type of bullet
                if (parentList == null || (parentList.IsOrdered != isOrdered  ||
                    ((isOrdered && parentList.OrderedDelimiter != orderedDelimiter) ||
                    (!isOrdered && parentList.BulletChar != bulletChar))))
                {
                    parentList = new ListBlock()
                    {
                        IsOrdered =  isOrdered,
                        BulletChar = bulletChar,
                        OrderedDelimiter = orderedDelimiter,
                        OrderedStart = orderedStart,
                    };
                }

                // A list is loose if any of its constituent list items are separated by blank lines, 
                // or if any of its constituent list items directly contain two block-level elements with a blank line between them. 
                // Otherwise a list is tight. (The difference in HTML output is that paragraphs in a loose list are wrapped in <p> tags, while paragraphs in a tight list are not.)
                if (parentList.consecutiveBlankLines > 0)
                {
                    parentList.IsLoose = true;
                    parentList.consecutiveBlankLines = 0;
                }

                // A list item can begin with at most one blank line
                if (numberOfSpaces < 0)
                {
                    parentList.consecutiveBlankLines = 1;
                }

                parentList.Children.Add(listItem);
                listItem.Parent = parentList;

                state.Block = listItem;

                return MatchLineResult.Continue;
            }

            public override void Close(MatchLineState state)
            {
                var listItem = state.Block as ListItemBlock;
                if (listItem != null)
                {
                    if (listItem.Children.Count > 1 && listItem.IsFollowedByBlankLine)
                    {
                        ((ListBlock)listItem.Parent).IsLoose = true;
                    }
                }
            }
        }
    }
}