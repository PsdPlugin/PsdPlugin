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
  public static class PsdPluginResources
  {
    private static ResourceManager rm =
      new ResourceManager("Photoshop.Resources", typeof(PsdPluginResources).Assembly);

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
      if (s != null)
        return s;

      // If no translation is available, fall back to the untagged resource
      return rm.GetString(resourceName);
    }
  }
}
