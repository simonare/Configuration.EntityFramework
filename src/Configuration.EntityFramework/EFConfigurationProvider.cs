﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Configuration.EntityFramework
{
    public class EFConfigurationProvider : ConfigurationProvider
    {
        public EFConfigurationProvider(Action<DbContextOptionsBuilder> optionsAction, string application = null, string discriminator = null, string aspect = "settings", bool ensureCreated = false)
        {
            this.OptionsAction = optionsAction;
            this.Application = application;
            this.Discriminator = discriminator;
            this.Aspect = aspect;
            this.EnsureCreated = ensureCreated;
        }

        public EFConfigurationProvider(ConfigurationContext context, string application = null, string discriminator = null, string aspect = "settings", bool ensureCreated = false)
        {
            this.Context = context;
            this.Application = application;
            this.Discriminator = discriminator;
            this.Aspect = aspect;
            this.EnsureCreated = ensureCreated;
        }

        protected virtual string Application { get; set; }

        protected virtual string Aspect { get; set; }

        protected virtual string Discriminator { get; set; }

        protected virtual Action<DbContextOptionsBuilder> OptionsAction { get; set; }

        protected virtual ConfigurationContext Context { get; set; }

        protected virtual bool IsContextOwner { get; set; }

        protected virtual bool EnsureCreated { get; set; }

        public override void Load()
        {
            this.Data = new Dictionary<string, string>();
            if (this.Context == null)
            {
                if (this.OptionsAction != null)
                {
                    var builder = new DbContextOptionsBuilder<ConfigurationContext>();
                    this.OptionsAction(builder);
                    this.Context = new ConfigurationContext(builder.Options);
                }
                else
                {
                    this.Context = new ConfigurationContext();
                }
                if (EnsureCreated) this.Context.Database.EnsureCreated();
                this.IsContextOwner = true;
            }
            try
            {
                var sections = this.Context.Sections.Where(s =>
                        string.IsNullOrEmpty(this.Application) || (s.ApplicationName == this.Application)
                        && string.IsNullOrEmpty(this.Aspect) || (s.Aspect == this.Aspect))
                    .Include(s => s.Settings);

                var filtered = this.FilterSectionsByDescriminator(sections, this.Discriminator);

                foreach (var section in filtered)
                {
                    Debug.WriteLine($"Adding Section with Id '{section.Id}' and Name '{section.SectionName}' to Configuration Provider");

                    foreach (var setting in section.Settings)
                    {
                        Debug.WriteLine($"Adding Setting with Id '{setting.Id}' and Key '{setting.Key}' and Value '{setting.Json}' to Configuration Provider");

                        if (this.IsJsonObjectOrArray(setting.Json))
                        {
                            var data = JsonConvert.DeserializeObject(setting.Json);
                            var container = (JContainer)data;
                            this.AddJObjectToData(section.SectionName, container);
                        }
                        else
                        {
                            this.AddSetting(setting.Key, setting.Json);
                        }
                    }
                }
            }
            finally
            {
                if (this.IsContextOwner && this.Context != null)
                {
                    this.Context.Dispose();
                    this.Context = null;
                }
            }
        }

        protected virtual bool IsJsonObjectOrArray(string value)
        {
            value = value.Trim();
            if ((value.StartsWith("{") && value.EndsWith("}")) || //For object
                (value.StartsWith("[") && value.EndsWith("]"))) //For array
            {
                return true;
            }
            return false;
        }

        protected virtual IEnumerable<SectionEntity> FilterSectionsByDescriminator(IEnumerable<SectionEntity> sections, string discriminator)
        {
            if (string.IsNullOrEmpty(discriminator))
            {
                return sections;
            }
            var filtered = new Collection<SectionEntity>();
            var kvp = JsonConvert.DeserializeObject<Dictionary<string, string>>(discriminator);
            if (kvp != null && kvp.Any())
            {          
                foreach (var section  in sections)
                {
                    if (this.HasDescriminator(section, kvp))
                    {
                        filtered.Add(section);
                    }
                }
            }
            else
            {
                Debug.WriteLine($"Discriminator '{discriminator}' could not deserialize into 'Dictionary<string, string>'. Check discriminator is valid json formatted string");
            }
            return filtered;
        }

        protected virtual bool HasDescriminator(SectionEntity section, Dictionary<string, string> discriminator)
        {
            if (string.IsNullOrEmpty(section.Discriminator))
            {
                return false;
            }
            var compare = JsonConvert.DeserializeObject<Dictionary<string, string>>(section.Discriminator);
            if (compare != null && compare.Any())
            {
                Debug.WriteLine($"Discriminator for section with Id '{section.Id}' and Name '{section.SectionName}' could not deserialize into 'Dictionary<string, string>'. Check discriminator is valid json formatted string");
            }
            foreach (var kvp in discriminator)
            {
                if (!compare.Any(e => e.Equals(kvp))) return false;
            }
            return true;
        }

        protected virtual void AddJObjectToData(string section, JContainer json)
        {
            foreach (var kvp in (JObject)json)
            {
                if (kvp.Value is JObject)
                {
                    this.AddJObjectToData($"{section}:{kvp.Key}", (JObject)kvp.Value);
                }
                else if (kvp.Value is JArray)
                {
                    var array = (JArray)kvp.Value;
                    var i = 0;
                    foreach (var item in array.OfType<JObject>())
                    {              
                        this.AddJObjectToData($"{section}:{kvp.Key}:{i}", item);
                        i++;
                    }
                }
                else
                {
                    var value = kvp.Value.Type != JTokenType.Null ? kvp.Value.ToString() : null;
                    this.AddSetting($"{section}:{kvp.Key}", value);
                }
            }
        }

        protected virtual void AddSetting(string key, string value)
        {
            if (!this.Data.ContainsKey(key)) this.Data.Add(key, value);
        }
    }
}