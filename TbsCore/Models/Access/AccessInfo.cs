﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RandomUserAgent;

namespace TbsCore.Models.Access
{
    public class AccessInfo
    {
        public List<Access> AllAccess { get; set; }
        public int CurrentAccess { get; set; }

        public Access GetCurrentAccess()
        {
            return AllAccess.ElementAtOrDefault(CurrentAccess);
        }

        public void Init()
        {
            AllAccess = new List<Access>();
        }

        public async Task<Access> GetNewAccess()
        {
            //await AccountHelper.CheckProxies(AllAccess);
            CurrentAccess++;

            if (CurrentAccess >= AllAccess.Count) CurrentAccess = 0;

            var access = GetCurrentAccess();
            access.LastUsed = DateTime.Now;

            return access;
        }

        public void AddNewAccess(Access access)
        {
            AllAccess.Add(access);
        }

        public void AddNewAccess(AccessRaw raw)
        {
            var access = new Access
            {
                Password = raw.Password,
                Proxy = raw.Proxy,
                ProxyPort = raw.ProxyPort,
                ProxyUsername = raw.ProxyUsername,
                ProxyPassword = raw.ProxyPassword,
                IsSittering = false,
                UserAgent = RandomUa.RandomUserAgent,
                LastUsed = DateTime.MinValue
            };

            AllAccess.Add(access);
        }
    }
}