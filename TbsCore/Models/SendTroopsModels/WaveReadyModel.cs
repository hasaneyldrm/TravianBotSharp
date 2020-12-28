﻿using System;
using System.Net.Http;

namespace TbsCore.Models.SendTroopsModels
{
    public class WaveReadyModel
    {
        /// <summary>
        /// Content of the wave request
        /// </summary>
        public FormUrlEncodedContent Content { get; set; }
        /// <summary>
        /// Time it takes for troops to arrive at the destination
        /// </summary>
        public TimeSpan MovementTime { get; set; }
    }
}
