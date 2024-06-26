﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Xml;

namespace MSR.CVE.BackMaker
{
    internal class BuildConfig
    {
        public static BuildConfig theConfig;
        public string buildConfiguration = "Broken";

        private readonly CfgString _editionName = new CfgString("editionName", "Broken");
        private readonly CfgBool _debugModeEnabled = new CfgBool("debugModeEnabled", false);
        private readonly CfgBool _forceAffineControlVisible = new CfgBool("forceAffineControlVisible", true);
        private readonly CfgBool _enableS3 = new CfgBool("enableS3", false);
        private readonly CfgInt _autoMaxZoomOffset = new CfgInt("autoMaxZoomOffset", 0);
        private readonly CfgBool _usingManifests = new CfgBool("usingManifests", false);
        private readonly CfgBool _debugRefs = new CfgBool("debugRefs", false);
        private readonly CfgBool _logInteractiveRenders = new CfgBool("logInteractiveRenders", false);
        private readonly CfgString _allFilesOption = new CfgString("allFilesOption", "");
        private readonly CfgBool _suppressFoxitMessages = new CfgBool("suppressFoxitMessages", false);
        private readonly CfgBool _enableSnapFeatures = new CfgBool("enableSnapFeatures", false);
        private readonly CfgString _veFormatUpdateURL = new CfgString("veFormatUpdateURL", null);
        private readonly CfgBool _injectTemporaryTileFailures = new CfgBool("injectTemporaryTileFailures", false);
        private readonly CfgInt _debugLevel = new CfgInt("debugLevel", 0);
        private readonly CfgString _mapControl = new CfgString("mapControl", null);
        private readonly CfgString _hostHome = new CfgString("hostHome", null);
        private readonly CfgString _mapCruncherHomeSite = new CfgString("mapCruncherHomeSite", null);
        private Dictionary<string, ParseableCfg> _configurationDict;

        public string editionName
        {
            get
            {
                return _editionName.value;
            }
            set
            {
                _editionName.value = value;
            }
        }

        public bool debugModeEnabled
        {
            get
            {
                return _debugModeEnabled.value;
            }
            set
            {
                _debugModeEnabled.value = value;
            }
        }

        public bool forceAffineControlVisible
        {
            get
            {
                return _forceAffineControlVisible.value;
            }
            set
            {
                _forceAffineControlVisible.value = value;
            }
        }

        public bool enableS3
        {
            get
            {
                return _enableS3.value;
            }
            set
            {
                _enableS3.value = value;
            }
        }

        public int autoMaxZoomOffset
        {
            get
            {
                return _autoMaxZoomOffset.value;
            }
            set
            {
                _autoMaxZoomOffset.value = value;
            }
        }

        public bool usingManifests
        {
            get
            {
                return _usingManifests.value;
            }
            set
            {
                _usingManifests.value = value;
            }
        }

        public bool debugRefs
        {
            get
            {
                return _debugRefs.value;
            }
            set
            {
                _debugRefs.value = value;
            }
        }

        public bool logInteractiveRenders
        {
            get
            {
                return _logInteractiveRenders.value;
            }
            set
            {
                _logInteractiveRenders.value = value;
            }
        }

        public string allFilesOption
        {
            get
            {
                return _allFilesOption.value;
            }
            set
            {
                _allFilesOption.value = value;
            }
        }

        public bool suppressFoxitMessages
        {
            get
            {
                return _suppressFoxitMessages.value;
            }
            set
            {
                _suppressFoxitMessages.value = value;
            }
        }

        public bool enableSnapFeatures
        {
            get
            {
                return _enableSnapFeatures.value;
            }
            set
            {
                _enableSnapFeatures.value = value;
            }
        }

        public string veFormatUpdateURL
        {
            get
            {
                return _veFormatUpdateURL.value;
            }
            set
            {
                _veFormatUpdateURL.value = value;
            }
        }

        public bool injectTemporaryTileFailures
        {
            get
            {
                return _injectTemporaryTileFailures.value;
            }
            set
            {
                _injectTemporaryTileFailures.value = value;
            }
        }

        public int debugLevel
        {
            get
            {
                return _debugLevel.value;
            }
            set
            {
                _debugLevel.value = value;
            }
        }

        public string mapControl
        {
            get
            {
                return _mapControl.value;
            }
            set
            {
                _mapControl.value = value;
            }
        }

        public string hostHome
        {
            get
            {
                return _hostHome.value;
            }
            set
            {
                _hostHome.value = value;
            }
        }

        public string mapCruncherHomeSite
        {
            get
            {
                return _mapCruncherHomeSite.value;
            }
            set
            {
                _mapCruncherHomeSite.value = value;
            }
        }

        private Dictionary<string, ParseableCfg> configurationDict
        {
            get
            {
                if (_configurationDict != null)
                {
                    return _configurationDict;
                }

                _configurationDict = new Dictionary<string, ParseableCfg>();
                AddCfg(_editionName);
                AddCfg(_debugModeEnabled);
                AddCfg(_forceAffineControlVisible);
                AddCfg(_enableS3);
                AddCfg(_autoMaxZoomOffset);
                AddCfg(_usingManifests);
                AddCfg(_debugRefs);
                AddCfg(_logInteractiveRenders);
                AddCfg(_allFilesOption);
                AddCfg(_suppressFoxitMessages);
                AddCfg(_enableSnapFeatures);
                AddCfg(_veFormatUpdateURL);
                AddCfg(_injectTemporaryTileFailures);
                AddCfg(_debugLevel);
                AddCfg(_hostHome);
                AddCfg(_mapCruncherHomeSite);

                return _configurationDict;
            }
        }

        public static Stream OpenConfigFile(string name)
        {
            string codeBase = Assembly.GetExecutingAssembly().GetName().CodeBase;
            string path = Uri.UnescapeDataString(new Uri(codeBase).AbsolutePath);
            string directoryName = Path.GetDirectoryName(path);
            if (directoryName == null)
            {
                return null;
            }

            string path2 = Path.Combine(directoryName, name);
            return new FileStream(path2, FileMode.Open, FileAccess.Read);
        }

        public static void Initialize()
        {
            try
            {
                Stream inStream = null;
                string name = "MapCruncherAppConfig.xml";
                try
                {
                    inStream = OpenConfigFile(name);
                }
                catch
                {
                    // ignored
                }

                var xmlDocument = new XmlDocument();
                if (inStream != null)
                {
                    xmlDocument.Load(inStream);
                }

                var xmlNode = xmlDocument.GetElementsByTagName("Build")[0];
                if (xmlNode?.Attributes == null)
                {
                    return;
                }

                string value = xmlNode.Attributes["Configuration"].Value;
                BuildConfig buildConfig;
                if (value == "MSR" || value == "Development")
                {
                    buildConfig = MSRConfig(value);
                }
                else
                {
                    buildConfig = VEConfig();
                }

                foreach (XmlNode xmlNode2 in xmlDocument.GetElementsByTagName("Parameter"))
                {
                    if (xmlNode2.Attributes == null)
                    {
                        continue;
                    }

                    string value2 = xmlNode2.Attributes["Name"].Value;
                    string value3 = xmlNode2.Attributes["Value"].Value;
                    if (buildConfig.configurationDict.ContainsKey(value2))
                    {
                        try
                        {
                            buildConfig.configurationDict[value2].ParseFrom(value3);
                            continue;
                        }
                        catch (Exception ex)
                        {
                            D.Sayf(0,
                                "Unable to parse field {0} value {1}: {2}",
                                new object[] {value2, value3, ex.Message});
                            continue;
                        }
                    }

                    D.Sayf(0, "Unrecognized field name {0}", new object[] {value2});
                }

                if (xmlNode.Attributes["AutoMaxZoomOffset"] != null)
                {
                    buildConfig.autoMaxZoomOffset = Convert.ToInt32(xmlNode.Attributes["AutoMaxZoomOffset"].Value,
                        CultureInfo.InvariantCulture);
                }

                theConfig = buildConfig;
            }
            catch (Exception)
            {
                theConfig = AppDomain.CurrentDomain.SetupInformation.ApplicationName.EndsWith(".vshost.exe") ? MSRConfig("Development") : VEConfig();
            }
        }

        private static BuildConfig VEConfig()
        {
            return new BuildConfig
            {
                buildConfiguration = "VE",
                editionName = "Virtual Earth Platform Edition",
                debugModeEnabled = false,
                forceAffineControlVisible = false,
                usingManifests = false,
                suppressFoxitMessages = true,
                enableSnapFeatures = false,
                mapControl = "http://dev.virtualearth.net/mapcontrol/mapcontrol.ashx?v=6",
                hostHome = "http://dev.virtualearth.net/mapcontrol/v6/mapcruncher/",
                mapCruncherHomeSite = "http://www.mapcruncher.com/"
            };
        }

        private static BuildConfig MSRConfig(string name)
        {
            var buildConfig = new BuildConfig
            {
                buildConfiguration = name
            };
            buildConfig.editionName = buildConfig.buildConfiguration + " Edition Resurrection ;}";
            buildConfig.debugModeEnabled = true;
            buildConfig.forceAffineControlVisible = true;
            buildConfig.enableS3 = true;
            buildConfig.usingManifests = true;
            //buildConfig.logInteractiveRenders = (buildConfig.buildConfiguration == "Development");
            buildConfig.allFilesOption = "|All files (*.*)|*.*";
            buildConfig.enableSnapFeatures = true;
            buildConfig.veFormatUpdateURL = "http://research.microsoft.com/mapcruncher/AppData/VEUrlFormat-3.1.5.xml";
            buildConfig.debugLevel = 0;
            buildConfig.mapControl = "http://dev.virtualearth.net/mapcontrol/mapcontrol.ashx?v=5";
            buildConfig.hostHome = "http://research.microsoft.com/mapcruncher/scripts/v5.5/";
            buildConfig.mapCruncherHomeSite = "http://research.microsoft.com/mapcruncher/";
            return buildConfig;
        }

        private BuildConfig()
        {
        }

        private void AddCfg(ParseableCfg cfg)
        {
            _configurationDict[cfg.name] = cfg;
        }
    }
}
