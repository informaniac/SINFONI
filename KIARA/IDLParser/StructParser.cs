﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace KIARA
{
    /// <summary>
    /// Parses struct entries of an IDL and registers them as new types to the KTD. Struct parser creates KTD objects
    /// for every struct in the IDL, creates KTD Type Objects for the elements of a struct type and registeres the
    /// structs to the KTD by their names
    /// </summary>
    internal class StructParser
    {
        internal static StructParser Instance = new StructParser();

        /// <summary>
        /// When IDL parser parses a line initiating a struct definition, struct parser creates a new struct object
        /// with the name given in the IDL. Member definitions parsed afterwards are added to this struct until
        /// parsing the struct has finished
        /// </summary>
        /// <param name="structDefinition">Line from IDL defining name of the struct</param>
        internal void startStructParsing(string structDefinition)
        {
            Regex nameRegEx = new Regex("struct ([A-Za-z0-9_]*) [{}._<,>; ]*");
            Match nameMatch = nameRegEx.Match(structDefinition);
            string name = nameMatch.Groups[1].Value;
            currentlyParsedStruct = new KtdStruct(name);
        }

        /// <summary>
        /// Receives one line of the struct and parses it as KTD Typed member object of the struct, registering
        /// it as one of the structs "members" with the Type specified in the IDL
        /// </summary>
        /// <param name="line"></param>
        internal void parseLineOfStruct(string line)
        {
            // Line contains a closing bracket. In this case, struct definition is finished
            // and parsing for the current struct can be finished
            if (line.Contains('}'))
            {
                handleLastLineOfStruct(line);
            }
            else
            {
                createKtdTypeForMember(line, currentlyParsedStruct);
            }
        }

        /// <summary>
        /// When Struct Parser encounters the closing bracket ('}') during the parsing of a struct, it finishes
        /// parsing the current struct object. The line may have content in addition to the bracket, e.g. when the
        /// last member definition is written in the same line.
        /// </summary>
        /// <param name="line">Last line of struct definition that closes struct with closing bracket</param>
        internal void handleLastLineOfStruct(string line)
        {
            // The closing bracket may appear at the end of the actual last line of the struct definition. Treat the
            // content of the line up to the closing bracket as member definition and try to parse it
            if (line.IndexOf('}') > 0)
            {
                string lastLine = line.Split('}')[0].Trim();
                createKtdTypeForMember(lastLine, currentlyParsedStruct);
                // finalize parsing the struct after having parsed the last line.
                finalizeStructParsing();
            }
            // If the closing bracket is the first character in line, no new information is added to the struct.
            else
            {
                finalizeStructParsing();

                // If the closing bracket is the first character in the line, but there is more content after that,
                // treat the remaining content as content of a new line.
                if (line.Length > 1)
                    IDLParser.Instance.parseLine(line.Split('}')[1]);
            }
        }

        /// <summary>
        /// Ends struct parsing mode of IDL parser and registers the parsed struct to the KTD
        /// </summary>
        internal void finalizeStructParsing()
        {
            IDLParser.Instance.currentlyParsing = KIARA.IDLParser.ParseMode.NONE;
            IDLParser.Instance.CurrentlyParsedKTD.RegisterType(currentlyParsedStruct);
        }


        /// <summary>
        /// Creates a member entry for a member specified in the IDL. A KtdType object of respective type is
        /// retrieved from the KTD or created for map or array typed members. Registers the member to the
        /// struct's members under the name given in the IDL
        /// </summary>
        /// <param name="memberDefinition">IDL entry defining type and name of the member</param>
        /// <param name="createdStruct">Struct that is created during the currrent parsing process</param>
        internal void createKtdTypeForMember(string memberDefinition, KtdStruct createdStruct)
        {
            if (!memberDefinition.Contains(';'))
                return;

            // Removes semicolon and comments at the end of line
            memberDefinition = memberDefinition.Trim().Split(';')[0];

            // TODO:
            // Consider additional spaces that may occur after comma of map definitions
            string[] memberComponents;
            string memberType;
            string memberName;

            if (!(memberDefinition.Contains("array") || memberDefinition.Contains("map")))
            {
                memberComponents = memberDefinition.Split(' ');
                memberType = memberComponents[0];
                memberName = memberComponents[1];
            }
            else
            {
                memberDefinition = Regex.Replace(memberDefinition, @"\s+", "");
                int closingBracket = memberDefinition.IndexOf('>') + 1;
                memberType = memberDefinition.Substring(0, closingBracket);
                memberName = memberDefinition.Substring(closingBracket, memberDefinition.Length - closingBracket);
            }

            KtdType typeObject = getTypeForMember(memberType);

            createdStruct.members.Add(memberName, typeObject);
        }

        /// <summary>
        /// Retrieves the respective KTD Type for a member type that is specified by its name in the IDL. Creates an
        /// Array or Map for array or map typed objects and queries the respective type from the KTD otherwise.
        /// </summary>
        /// <param name="memberType">Name of the member type as given in the IDL</param>
        /// <returns>KTD Type object with the given name</returns>
        private KtdType getTypeForMember(string memberType)
        {
            KtdType typeObject = new KtdType();

            if (memberIsArray(memberType))
            {
                typeObject = ArrayParser.Instance.ParseArray(memberType);
            }
            else if (memberIsMap(memberType))
            {
                typeObject = MapParser.Instance.ParseMap(memberType);
            }
            else
            {
                typeObject = IDLParser.Instance.CurrentlyParsedKTD.GetKtdType(memberType);
            }

            return typeObject;
        }

        /// <summary>
        /// Checks if a member is of array type
        /// </summary>
        /// <param name="memberType">Name of the member type as given in the IDL</param>
        /// <returns>true, if member is array</returns>
        private bool memberIsArray(string memberType)
        {
            bool isArray = Regex.IsMatch(memberType, "array<[A-Za-z0-9_]*>");
            return isArray;
        }

        /// <summary>
        /// Checks if a member is of map type
        /// </summary>
        /// <param name="memberType">Name of the member type as given in the IDL</param>
        /// <returns>true, if member is array</returns>
        private bool memberIsMap(string memberType)
        {
            bool isMap = Regex.IsMatch(memberType, "map<[A-Za-z0-9_]*,[A-Za-z0-9_]*>");
            return isMap;
        }

        KtdStruct currentlyParsedStruct;
    }
}
