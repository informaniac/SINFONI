﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using KIARA.Exceptions;

namespace KIARA
{
    /// <summary>
    /// Takes an IDL that is provided locally or at some remote place and translates its contents to an abstract
    /// representation of types and services. Registeres specified types to the KTD and specified services to
    /// the KIARA service registry and maps types specified in the IDL to the respective previously parsed
    /// KTD Types
    /// </summary>
    public class IDLParser
    {
        internal enum ParseMode
        {
            COMMENT,
            STRUCT,
            SERVICE,
            NONE
        }

        public KTD CurrentlyParsedKTD { get; internal set; }

        public static IDLParser Instance = new IDLParser();

        public KTD ParseIDLFromUri(string idlUri)
        {
            WebClient webClient = new WebClient();
            string idlContent = webClient.DownloadString(idlUri);
            return ParseIDL(idlContent);
        }

        public KTD ParseIDL(string idlString, KTD targetKTD)
        {
            CurrentlyParsedKTD = targetKTD;
            lineNumberParsed = 0;
            currentlyParsing = ParseMode.NONE;
            wasParsingBeforeComment = ParseMode.NONE;

            string[] idlLines =
                idlString.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string line in idlLines)
            {
                ++lineNumberParsed;
                parseLine(line.Trim());
            }

            return CurrentlyParsedKTD;
        }
        /// <summary>
        /// Parses a complete IDL
        /// </summary>
        /// <param name="idlString">Complete IDL</param>
        public KTD ParseIDL(string idlString)
        {
            return ParseIDL(idlString, new KTD());
        }

        /// <summary>
        /// Parses a single line of the IDL. Discards commented lines. Intiates parsing of struct or service
        /// definitions if line defines a new struct or service
        /// </summary>
        /// <param name="line">Line of the IDL that is parsed</param>
        internal void parseLine(string line)
        {
            if(lineIsComment(line))
                return;

            line = removeCommentedParts(line);

            if (line.Length == 0)
                return;

            if (currentlyParsing == ParseMode.NONE)
            {
                startObjectParsing(line);
            }
            else if (currentlyParsing == ParseMode.STRUCT)
            {
                StructParser.Instance.parseLineOfStruct(line);
            }
            else if (currentlyParsing == ParseMode.SERVICE)
            {
                ServiceParser.Instance.parseLineOfService(line, lineNumberParsed);
            }
        }

        /// <summary>
        /// Parser is currently not parsing an object (struct or service) and encounters a new line.
        /// The line will be interpreted as either struct or service definition, initiating the respective
        /// parsing. If the line cannot be matched to either struct or service definition, an exception
        /// is thrown.
        /// </summary>
        /// <param name="line">Line of the IDL that is parsed</param>
        private void startObjectParsing(string line)
        {
            if (line.Contains("struct") && line.IndexOf("struct") == 0)
            {
                currentlyParsing = ParseMode.STRUCT;
                StructParser.Instance.startStructParsing(line);
            }
            else if (line.Contains("service") && line.IndexOf("service") == 0)
            {
                currentlyParsing = ParseMode.SERVICE;
                ServiceParser.Instance.startServiceParsing(line);
            }
            else
            {
                throw new IDLParseException(line, lineNumberParsed);
            }
        }

        /// <summary>
        /// Checks if a line is actually entirely a comment (C-Style or C++ Style)
        /// </summary>
        /// <param name="line">Line of the IDL that is parsed</param>
        /// <returns>true, if the line is a comment</returns>
        private bool lineIsComment(string line)
        {
            if (line.Contains("/*"))
            {
                if (line.IndexOf("/*") == 0)
                {
                    wasParsingBeforeComment = currentlyParsing;
                    currentlyParsing = ParseMode.COMMENT;
                }
            }
            else if (line.Contains("*/"))
            {
                currentlyParsing = wasParsingBeforeComment;
            }
            return currentlyParsing == ParseMode.COMMENT
                || line.Contains("//") && line.IndexOf("//") == 0;
        }

        /// <summary>
        /// Lines may be partially commented out, either by commencing line comments at the end of the line, or by
        /// ending block comments within the line. Those comments are removed, and the remaining line is handled by
        /// the IDL parser
        /// </summary>
        /// <param name="line">Line of the IDL that contains a comment</param>
        /// <returns>Line without the commented part</returns>
        private string removeCommentedParts(string line)
        {
            if (line.Contains("//"))
            {
                line = line.Substring(0, line.IndexOf("//"));
            }

            if (line.Contains("/*"))
            {
                line = removeBlockCommentStartingInLine(line);
            }

            else if (line.Contains("*/"))
            {
                line = line.Substring(line.IndexOf("*/") + 2, line.Length - ((line.IndexOf("*/")+2)));
            }

            return line.Trim();
        }

        /// <summary>
        /// A block comment may start within a line, and either also end within this line, or span multiple lines
        /// starting in the current line. For the first case, the complete inline comment is removed, for the
        /// second, the entire rest of the line, with switching the parser to comment mode.
        /// </summary>
        /// <param name="line">Line of the IDL with starting block comment</param>
        /// <returns>Line without the block comment part</returns>
        private string removeBlockCommentStartingInLine(string line)
        {
            if (line.Contains("*/"))
            {
                line = line.Remove(line.IndexOf("/*"), line.IndexOf("*/") - line.IndexOf("/*") + 2);
            }
            else
            {
                line = line.Substring(0, line.IndexOf("/*"));
                wasParsingBeforeComment = currentlyParsing;
                currentlyParsing = ParseMode.COMMENT;
            }
            return line;
        }

        internal ParseMode currentlyParsing = ParseMode.NONE;
        internal ParseMode wasParsingBeforeComment = ParseMode.NONE;
        int lineNumberParsed = 0;
    }
}
