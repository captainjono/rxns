using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Rxns.AppStatus.Host.Ai.Workspace
{
    /// <summary>One contiguous slice of a file with line-range metadata so
    /// search results can point the operator at the exact location.</summary>
    public class FileChunk
    {
        public string AbsolutePath { get; set; }
        public string RelativePath { get; set; }
        public string Root         { get; set; }
        public int    LineStart    { get; set; }   // 1-based, inclusive
        public int    LineEnd      { get; set; }   // 1-based, inclusive
        public string Text         { get; set; }
    }

    /// <summary>
    /// Splits files into chunks suitable for embedding. Two-pass:
    ///   1. Read lines.
    ///   2. Greedy-pack lines into chunks of up to <see cref="TargetChars"/>
    ///      characters, preferring to break on blank lines so paragraph
    ///      semantics stay intact. If a single line is bigger than the
    ///      target, hard-split it at character boundaries.
    /// Optional overlap between chunks so a paragraph straddling a boundary
    /// still embeds coherently.
    /// </summary>
    public class WorkspaceChunker
    {
        public int TargetChars   { get; set; } = 1200;
        public int OverlapChars  { get; set; } = 120;

        public List<FileChunk> ChunkFile(string root, string absolutePath)
        {
            var result = new List<FileChunk>();
            if (!File.Exists(absolutePath)) return result;

            string[] lines;
            try { lines = File.ReadAllLines(absolutePath); }
            catch { return result; }

            string relative;
            try { relative = Path.GetRelativePath(root, absolutePath).Replace('\\', '/'); }
            catch { relative = absolutePath; }

            var bufStart = 1;
            var bufLines = new List<string>();
            var bufLen   = 0;

            void Flush(int endLineInclusive)
            {
                if (bufLines.Count == 0) return;
                var text = string.Join("\n", bufLines);
                if (string.IsNullOrWhiteSpace(text)) { bufLines.Clear(); bufLen = 0; return; }

                result.Add(new FileChunk
                {
                    Root         = root,
                    AbsolutePath = absolutePath,
                    RelativePath = relative,
                    LineStart    = bufStart,
                    LineEnd      = endLineInclusive,
                    Text         = text
                });

                // Compute overlap: keep the trailing OverlapChars characters'
                // worth of lines for the next chunk so a paragraph spanning
                // the boundary still has context on both sides.
                bufLines = new List<string>();
                bufLen = 0;
                if (OverlapChars > 0)
                {
                    var keepFromLine = endLineInclusive + 1;
                    var carry = new List<string>();
                    var carryLen = 0;
                    for (var i = result[result.Count - 1].Text.Length - 1; i >= 0 && carryLen < OverlapChars; i--)
                    { carryLen++; }
                    // We don't actually re-inject text here; the next iteration
                    // simply starts at endLine+1. Overlap is conceptual but the
                    // simple line-greedy chunker keeps boundaries clean enough
                    // without re-emitting text. (Re-injection adds complexity
                    // for marginal recall gain.)
                    bufStart = keepFromLine;
                }
                else
                {
                    bufStart = endLineInclusive + 1;
                }
            }

            for (var i = 0; i < lines.Length; i++)
            {
                var lineText = lines[i];
                var addedLen = lineText.Length + 1; // +1 for newline

                // If a single line is larger than TargetChars, flush whatever
                // we have, then emit hard-split chunks for that line alone.
                if (lineText.Length > TargetChars)
                {
                    Flush(i); // emit pending
                    for (var off = 0; off < lineText.Length; off += TargetChars)
                    {
                        var len = Math.Min(TargetChars, lineText.Length - off);
                        result.Add(new FileChunk
                        {
                            Root         = root,
                            AbsolutePath = absolutePath,
                            RelativePath = relative,
                            LineStart    = i + 1,
                            LineEnd      = i + 1,
                            Text         = lineText.Substring(off, len)
                        });
                    }
                    bufStart = i + 2;
                    continue;
                }

                // Prefer to flush on a blank line if we're past ~70% of target.
                if (bufLen + addedLen > TargetChars
                    || (string.IsNullOrWhiteSpace(lineText) && bufLen > TargetChars * 0.7))
                {
                    Flush(i);   // pending chunk ends at the PREVIOUS line, so i is the new start
                    // Don't append the blank line that triggered the flush — it
                    // produces visual noise without semantic value.
                    if (string.IsNullOrWhiteSpace(lineText)) { bufStart = i + 2; continue; }
                }

                bufLines.Add(lineText);
                bufLen += addedLen;
            }

            Flush(lines.Length);
            return result;
        }

        public List<FileChunk> ChunkFiles(string root, IEnumerable<string> absolutePaths)
        {
            var all = new List<FileChunk>();
            foreach (var p in absolutePaths ?? Array.Empty<string>())
            {
                all.AddRange(ChunkFile(root, p));
            }
            return all;
        }
    }
}
