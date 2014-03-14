namespace WebRole1
{
    using System;
    using System.Text;
    using System.Collections.Generic;

    public class Trie
    {
        const int maxResultNum = 10;
        const int maxWordNum = 20;
        private Node root = new Node(); // Root node shouldn't represent any key

        public Trie()
        {
            NumSuggestionsAvailable = 0;
        }

        public uint NumSuggestionsAvailable { get; private set; }

        public bool InsertPhrase(string phrase)
        {
            Node node = root;

            // Just return if the phrase doesn't only contain alphabet
            if (!IsPhraseValid(phrase))
            {
                return false;
            }

            // Replace all underscore characters with a space
            //phrase = phrase.Replace('_', ' ');

            foreach (char c in phrase.ToLower())
            {
                if (node.IsTerminal)
                {
                    // Insert node here
                    node = node.InsertChild(c);
                }
                else
                {
                    // Check whether a key already exists
                    if (node.ContainsKey(c))
                    {
                        // Move on to the next child
                        node = node.GetChildForKey(c);
                    }
                    else
                    {
                        // Insert node here
                        node = node.InsertChild(c);
                    }
                }
            }

            // The last node should be a result
            node.IsResult = true;

            NumSuggestionsAvailable++;

            return true;
        }

        public List<string> SearchPhrasesForPrefix(string prefix)
        {
            List<string> resultList = new List<string>();
            string result = string.Empty;
            Node node = root;

            if (!IsPhraseValid(prefix))
            {
                // Don't bother searching
                return resultList;
            }

            foreach (char c in prefix.ToLower()/*.Replace('_', ' ')*/)
            {
                if (node.ContainsKey(c))
                {
                    node = node.GetChildForKey(c);
                    result += c;
                }
                else
                {
                    // Return right away if prefix not found
                    return resultList;
                }
            }

            // Start searching
            SearchPhraseForPrefixHelper(ref resultList, node, result);

            return resultList;
        }

        private void SearchPhraseForPrefixHelper(ref List<string> resultList, Node node, string result)
        {
            if (resultList.Count == maxResultNum)
            {
                return;
            }

            if (node.IsResult)
            {
                // Add result to list
                resultList.Add(result);

                // Stop searching for enough number of results
                if (resultList.Count == maxResultNum)
                {
                    return;
                }
            }

            // Go through each child
            foreach (Node childNode in node.Children)
            {
                SearchPhraseForPrefixHelper(ref resultList, childNode, result + (char)childNode.Data);
            }
        }

        private bool IsPhraseValid(string phrase)
        {
            if (phrase.Split(' ').Length > maxWordNum)
            {
                return false;
            }

            //foreach (char c in phrase)
            //{
            //    if ((c != ' ') && (c != '_') && ((c < 'a') || (c > 'z')) && ((c < 'A') || (c > 'Z')))
            //    {
            //        return false;
            //    }
            //}

            return true;
        }


    }
}