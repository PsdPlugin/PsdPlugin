/////////////////////////////////////////////////////////////////////////////////
//
// Photoshop PSD FileType Plugin for Paint.NET
// http://psdplugin.codeplex.com/
//
// This software is provided under the MIT License:
//   Copyright (c) 2006-2007 Frank Blumenberg
//   Copyright (c) 2010-2012 Tao Yue
//
// See LICENSE.txt for complete licensing and attribution information.
//
/////////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Text;
using System.Threading;

using PaintDotNet;

namespace PaintDotNet.Data.PhotoshopFileType
{
  // TODO: It is not a correct way to store prefixed resources!
  public static class PsdPluginResources
  {
    private static ResourceManager rm =
      new ResourceManager("Photoshop.Resources", typeof(PsdPluginResources).Assembly);

    public static string LayersPalette_LayerGroupBegin = "LayersPalette_LayerGroupBegin";
    public static string LayersPalette_LayerGroupEnd = "LayersPalette_LayerGroupEnd";

    /// <summary>
    /// Get a collection of group name values for all languages.
    /// </summary>
    /// <returns></returns>
    public static List<string> GetAllLayerGroupNames(string resourceKey)
    {
      var toReturn = new List<string>();
      // While layer groups can be localized we should check all variations of localization.
      var resources = rm.GetResourceSet(new System.Globalization.CultureInfo("en"), false, true);
      if (resources != null)
      {
        foreach (System.Collections.DictionaryEntry entry in resources)
        {
          var key = entry.Key.ToString();
          if (key == resourceKey || key.StartsWith(resourceKey) || key.EndsWith(resourceKey))
          {
            var value = entry.Value as string;
            if (!string.IsNullOrEmpty(value) && value.Contains(':'))
            {
              var variantOfLocalizedLayer = value.Split(':')[0].Trim().ToLower();
              if (!string.IsNullOrEmpty(variantOfLocalizedLayer))
                toReturn.Add(variantOfLocalizedLayer);
            }
          }
        }
      }

      return toReturn;
    }

    public static string GetString(string resourceName)
    {
      // We really ought to use .Name, but .NET 3.5 returns legacy three-letter
      // region codes rather than the two-letter ISO 3166 codes that MSDN
      // claims it returns.  Since Paint.NET is currently translated into only
      // one region per language, we can get by without the region for now.
      var languageCode = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

      // We currently have very few localized strings, so it's not really worth
      // deploying satellite assemblies for each language.  For now, we simply
      // prefix the resource name with the language code.
      var taggedResourceName = languageCode + "_" + resourceName;
      var s = rm.GetString(taggedResourceName);
      if (!string.IsNullOrEmpty(s))
        return s;

      // If no translation is available, fall back to the untagged resource
      return rm.GetString(resourceName);
    }
  }
}
