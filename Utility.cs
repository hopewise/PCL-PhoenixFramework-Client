using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Phoenix
{
  internal static class Utility
  {
    //  static serialize(obj, parentKey){
    //  let queryStr = [];
    //  for(var key in obj){ if(!obj.hasOwnProperty(key)){ continue }
    //    let paramKey = parentKey ? `${parentKey}[${key}]` : key
    //    let paramVal = obj[key]
    //    if(typeof paramVal === "object"){
    //      queryStr.push(this.serialize(paramVal, paramKey))
    //    } else {
    //      queryStr.push(encodeURIComponent(paramKey) + "=" + encodeURIComponent(paramVal))
    //    }
    //  }
    //  return queryStr.join("&")
    //}

    internal static string Serialize(JObject json, string parentKey = null)
    {
      var paramsList = new List<string>();
      foreach (var jp in json.Children<JProperty>())
      {
        var key = jp.Name;
        var paramKey = (parentKey != null) ? $"{parentKey}[{key}]" : key;

        if (jp.Value.Type == JTokenType.Object)
        {
          var serialized = Serialize((JObject)jp.Value, paramKey);
          paramsList.Add(serialized);
        }
        else
        {
          var encodedKey = WebUtility.UrlEncode(paramKey);
          var encodedValue = WebUtility.UrlEncode(jp.Value.ToString());
          paramsList.Add($"{encodedKey}={encodedValue}");
        }
      }

      return String.Join("&", paramsList.ToArray());
    }

    //static appendParams(url, params){
    //  if(Object.keys(params).length === 0){ return url }

    //  let prefix = url.match(/\?/) ? "&" : "?"
    //  return `${url}${prefix}${this.serialize(params)}`
    //}
    internal static string AppendParams(string url, JObject params_)
    {
      if (params_ == null || params_.Children().Count() < 1) return url;

      var prefix = url.Contains("?") ? "&" : "?";
      var serialized = Serialize(params_);

      return $"{url}{prefix}{serialized}";
    }
  }
}