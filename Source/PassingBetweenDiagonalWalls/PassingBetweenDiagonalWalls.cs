using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Verse;

namespace PassingBetweenDiagonalWalls
{
    [StaticConstructorOnStartup]
    public class PassingBetweenDiagonalWalls : Mod
    {
        public PassingBetweenDiagonalWalls(ModContentPack content) : base(content)
        {
            PassingBetweenDiagonalWalls.content = content;
            PassingBetweenDiagonalWalls.ExposeData();
        }

        public static ModContentPack content;

        public static List<string> diagonalWallDefNames;

        public static void ExposeData()
        {
            using (StreamReader streamReader = new StreamReader(content.RootDir + "/DiagonalWallList.xml"))
            {
                using (XmlTextReader xmlTextReader = new XmlTextReader(streamReader))
                {
                    XmlDocument xmlDocument = new XmlDocument();
                    xmlDocument.Load(xmlTextReader);
                    var curXmlParent = xmlDocument.DocumentElement;

                    PassingBetweenDiagonalWalls.diagonalWallDefNames = new List<string>(curXmlParent.ChildNodes.Count);
                    foreach (var node in curXmlParent.ChildNodes)
                    {
                        var name = ScribeExtractor.ValueFromNode<string>((XmlNode)node, default(string));
                        PassingBetweenDiagonalWalls.diagonalWallDefNames.Add(name);
                    }
                }
            }
        }
    }
}
