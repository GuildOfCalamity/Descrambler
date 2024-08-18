using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace Descrambler
{
    public class Unscramble
    {
        static bool _dictionaryLoaded = false;
        static string _wordToUnscramble = "";
        static int _totalEntries = 0;
        static Dictionary<string, string> _sortedDictionary = new Dictionary<string, string>();
        static List<string> _results = new List<string>();
        static Stopwatch _stopwatch;

        /// <summary>
        /// We don't really need a constructor
        /// </summary>
        //public Unscramble(string wordToUnscramble)
        //{
        //    _WordToUnscramble = wordToUnscramble;
        //}

        public List<string> UnscrambleWord(string wordToUnscramble, bool useFiltering = true)
        {
            _stopwatch = Stopwatch.StartNew();
            if (string.IsNullOrEmpty(_wordToUnscramble))
            {
                _wordToUnscramble = wordToUnscramble;
            }
            else if (!_wordToUnscramble.Equals(wordToUnscramble, StringComparison.OrdinalIgnoreCase) && useFiltering)
            {   //If re-using the object and the word is different, we'll need to reload the dictionary
                _dictionaryLoaded = false;
                _wordToUnscramble = wordToUnscramble;
                _results.Clear();
            }
            else if (_wordToUnscramble.Equals(wordToUnscramble, StringComparison.OrdinalIgnoreCase))
            {
                _results.Clear(); //we should clear the results array so they don't stack
            }

            if (!_dictionaryLoaded) //the first call will be slightly slower
                LoadEmbeddedDictionary(wordToUnscramble.ToUpper(), useFiltering);

            string scrambleSorted = SortWord(wordToUnscramble.ToUpper());
            //var kvp = SortedDictionary.FirstOrDefault(p => SortedDictionary.Comparer.Equals(p.Value, scrambledSort));
            var matchList = _sortedDictionary.Where(kvp => kvp.Value == scrambleSorted).Select(kvp => kvp.Key).ToList();
            if (matchList.Count > 0)
            {
                foreach (string result in matchList)
                {
                    Debug.WriteLine($"⇒ Match: {result}");
                    _results.Add(result);
                }
                _stopwatch.Stop();
                Debug.WriteLine($"⇒ Elapsed time: {_stopwatch.Elapsed}");
                return _results;
            }
            else //no matches
            {
                _stopwatch.Stop();
                _results.Clear();
                Debug.WriteLine($"⇒ Elapsed time: {_stopwatch.Elapsed}");
                return _results;
            }
        }

        public TimeSpan GetMatchTime() => _stopwatch.Elapsed;
        public List<string> GetMatchResults() => _results;
        public int GetMatchCount() => _results.Count;
        public int GetFilterCount() => _sortedDictionary.Count;
        public int GetDictionaryCount() => _totalEntries;
        static string SortWord(string str) => String.Concat(str.OrderBy(c => c));
        static void LoadEmbeddedDictionary(string wordText, bool filter = false)
        {
            char[] delims = new char[1] { '\n' };
            string[] chunks;
            int chunkCount = 0;
            //string data = BitConverter.ToString(Descrambler.Properties.Resources.Dictionary);
            string data = Encoding.UTF8.GetString(Descrambler.Properties.Resources.Dictionary, 0, Descrambler.Properties.Resources.Dictionary.Length);
            chunks = data.ToUpper().Split(delims);
            Debug.WriteLine($"⇒ Length filter: {wordText.Length}");
            _sortedDictionary.Clear();
            foreach (string str in chunks)
            {
                chunkCount++;
                // We're assuming the word will have at least 3 characters.
                // I mean would you really need this program if it was only two?
                if (wordText.Length >= 3)
                {
                    if ((str.Length == wordText.Length) && str.Contains(wordText.Substring(0, 1)) && str.Contains(wordText.Substring(1, 1)) && str.Contains(wordText.Substring(2, 1))) //just checking the 1st, 2nd & 3rd letter will trim our search considerably
                    {
                        try { _sortedDictionary.Add(str, SortWord(str)); }
                        catch (Exception) { /* probably a key collision, just ignore */ }
                    }
                }
            }
            Debug.WriteLine($"⇒ Loaded {_sortedDictionary.Count} possible matches out of {chunkCount.ToString()}");
            _totalEntries = chunkCount;
            _dictionaryLoaded = true;
        }
    }
}
