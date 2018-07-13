using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using System;
using System.ComponentModel;
using System.Numerics;
using System.Collections.Generic;
using Helper = Neo.SmartContract.Framework.Helper;

namespace MySky
{

    /// <summary>
    /// 部署合约
    /// ParameterList:0710
    /// ReturnType:05
    /// 
    /// 错误码
    /// 0x01 操作成功
    /// 0xEE 权限不足
    /// 0x11 飞机ID查找失败 TokenData/OwnerOf/ChangeOwner/Upgrade
    /// 0x12 飞机owner错误，没有操作权限 ChangeOwner/Upgrade/
    /// 0x13 升级飞机等级错误 Upgrade
    /// 0x14 该颜色飞机库存不足 Init
    /// 0x15 重复创建同ID飞机 Init
    /// 0x16 购买的飞机不在拍卖中 ChangeOwner
    /// 0x17 更新的飞机正在拍卖中 Upgrade
    /// 
    /// </summary>

    public class MySkyContract : SmartContract
    {
        public class Aircraft
        {
            // 飞机id, 在map中作为key存储
            public BigInteger Id;

            // 该飞机的拥有者的地址，初代飞机也有拥有者，即系统账号
            public byte[] owner;

            // 整机升级
            public int level;

            // 拍卖截止时间
            // 放入拍卖，即更新拍卖时间
            // 在拍卖时间内的飞机视为 正在拍卖中
            // 过了拍卖，则不在拍卖中，并且玩家可操作
            public uint auctionTime;

            // 拍卖金额，只有飞机在拍卖中，该值才有效
            public int price;
        }

        public class AircraftStatic
        {
            public BigInteger[] aircraftQuota;
            public BigInteger[] currentCount;
        }

        #region notify
        // 生成新飞机
        public delegate void deleBirth(BigInteger tokenId, byte[] owner);
        [DisplayName("birth")]
        public static event deleBirth BirthedLog;

        // 升级新飞机
        public delegate void deleUpgradeLevel(BigInteger tokenId, byte[] owner, int newLevel);
        [DisplayName("upgrade")]
        public static event deleUpgradeLevel UpgradeLevelLog;

        // 转移所属
        public delegate void deleChangeOwner(BigInteger tokenId, byte[] fromOwner, byte[] toOwner);
        [DisplayName("changeOwner")]
        public static event deleChangeOwner ChangeOwnerLog;

        // 放入拍卖池
        public delegate void deleInAuction(BigInteger tokenId, byte[] owner, int price, BigInteger auctionDueTime);
        [DisplayName("inAuction")]
        public static event deleInAuction InAuctionLog;

        // 从拍卖池中取出
        public delegate void deleOutAuction(BigInteger tokenId, byte[] owner);
        [DisplayName("outAuction")]
        public static event deleOutAuction OutAuctionLog;
        #endregion

        // token function   
        public static string Name() => "MYSKY coin";
        public static string Symbol() => "MYSKY";
        public static readonly byte[] SuperAdmin = "AKpsjfBu5p88WPkGFWQwAVHXiCA789ikzL".AsByteArray();
        
        public static readonly byte[] ContractOwner = "AKpsjfBu5p88WPkGFWQwAVHXiCA789ikzL".ToScriptHash();
        public static readonly byte[] ContractOwner1 = "AYxLMGBnNsYcaxVgHymBXjN6GKm7vPPy2z".ToScriptHash();

        // 2018/5/1 00:00:00
        //private static BigInteger InitAuctionTimestamp = 1525104000;
        private const string KEY_TOTAL = "totalSupply";
        private const string tokenStoragePrefix = "air/";

        public static int CountLimit(BigInteger color)
        {
            /// 飞机上限
            /// 颜色    上限
            /// 1(白色) 1-99999
            /// 2(绿色) 1-99999
            /// 3(蓝色) 1-49999
            /// 4(紫色) 1-9999
            /// 5(红色) 1-999
            /// 6(金色) 1-99
            if(color == 1 || color == 2)
            {
                return 99999;
            }
            else if(color == 3)
            {
                return 49999;
            }
            else if(color == 4)
            {
                return 9999;
            }
            else if (color == 5)
            {
                return 999;
            }
            else if (color == 6)
            {
                return 99;
            }
            else
            {
                return 0;
            }
        }

        public static BigInteger TotalSupply()
        {
            return Storage.Get(Storage.CurrentContext, KEY_TOTAL).AsBigInteger();
        }

        public static Object Main(string method, object[] args)
        {
            string magic = "TCG-2018-05-08";
            // 必须在入口函数取得callscript，调用脚本的函数，也会导致执行栈变化，再取callscript就晚了
            var callscript = ExecutionEngine.CallingScriptHash;

            if (method == "tokenData")
            {
                BigInteger tokenId = (BigInteger)args[0];
                return TokenData(tokenId);
            }
            if (method == "ownerOf")
            {
                BigInteger tokenId = (BigInteger)args[0];
                return OwnerOf(tokenId);
            }
            if (method == "quota")
            {
                return AircraftQuota();
            }
            // 所有写调用都只允许超级管理员
            // TODO: 调试时移除该权限检查，上线时添加
            //if (!Runtime.CheckWitness(ContractOwner) && !Runtime.CheckWitness(ContractOwner1))
            //{
            //    return new byte[] { 0xEE };
            //}
            if (method == "initTcgAircraftWorld")
            {
                return InitTcgAircraftWorld();
            }
            if (method == "initAircraftBySys")
            {
                BigInteger tokenPrefix = (BigInteger)args[0];
                return InitAircraft(tokenPrefix, SuperAdmin);
            }
            if (method == "initAircraftByUser")
            {
                BigInteger tokenPrefix = (BigInteger)args[0];
                byte[] owner = (byte[])args[1];
                return InitAircraft(tokenPrefix, owner);
            }
            if (method == "upgrade")
            {
                BigInteger tokenId = (BigInteger)args[0];
                byte[] owner = (byte[])args[1];
                int fromLevel = (int)args[2];

                return Upgrade(tokenId, owner, fromLevel);
            }
            if (method == "changeOwner")
            {
                BigInteger tokenId = (BigInteger)args[0];
                byte[] fromOwner = (byte[])args[1];
                byte[] toOwner = (byte[])args[2];

                return ChangeOwner(tokenId, fromOwner, toOwner);
            }
            if(method == "inAuction")
            {
                BigInteger tokenId = (BigInteger)args[0];
                byte[] owner = (byte[])args[1];
                int price = (int)args[2];
                // 固定放入拍卖池的时间 - 3天
                return InAuction(tokenId, owner, price);
            }
            if (method == "outAuction")
            {
                BigInteger tokenId = (BigInteger)args[0];
                byte[] owner = (byte[])args[1];
                return OutAuction(tokenId, owner);
            }

            return new byte[] { 0xFF };
        }

        public static Object AircraftQuota()
        {
            BigInteger[] aircraftQuota = new BigInteger[6];
            aircraftQuota[0] = 99999;
            aircraftQuota[1] = 99999;
            aircraftQuota[2] = 49999;
            aircraftQuota[3] = 9999;
            aircraftQuota[4] = 999;
            aircraftQuota[5] = 99;

            BigInteger[] currentCount = new BigInteger[6];
            currentCount[0] = Storage.Get(Storage.CurrentContext, _getAircraftSupplyStorageKey(1)).AsBigInteger();
            currentCount[1] = Storage.Get(Storage.CurrentContext, _getAircraftSupplyStorageKey(2)).AsBigInteger();
            currentCount[2] = Storage.Get(Storage.CurrentContext, _getAircraftSupplyStorageKey(3)).AsBigInteger();
            currentCount[3] = Storage.Get(Storage.CurrentContext, _getAircraftSupplyStorageKey(4)).AsBigInteger();
            currentCount[4] = Storage.Get(Storage.CurrentContext, _getAircraftSupplyStorageKey(5)).AsBigInteger();
            currentCount[5] = Storage.Get(Storage.CurrentContext, _getAircraftSupplyStorageKey(6)).AsBigInteger();

            AircraftStatic aircraftStatic = new AircraftStatic();
            aircraftStatic.aircraftQuota = aircraftQuota;
            aircraftStatic.currentCount = currentCount;
            return aircraftStatic;
        }

        public static byte[] OutAuction(BigInteger tokenId, byte[] owner)
        {
            byte[] aircraftInfo = Storage.Get(Storage.CurrentContext, tokenId.AsByteArray());
            if (aircraftInfo.Length == 0)
            {
                OutAuctionLog(-1, owner);
                return new byte[] { 0x11 };
            }
            Aircraft aircraft = (Aircraft)Helper.Deserialize(aircraftInfo);
            if (aircraft.owner != owner)
            {
                OutAuctionLog(-1, owner);
                return new byte[] { 0x12 };
            }

            // 赋予一个较早的时间戳,保证过期
            aircraft.auctionTime = 1525104000;
            OutAuctionLog(tokenId, owner);

            return new byte[] { 0x01 };
        }

        public static byte[] InAuction(BigInteger tokenId, byte[] owner, int price)
        {
            byte[] aircraftInfo = Storage.Get(Storage.CurrentContext, tokenId.AsByteArray());
            if (aircraftInfo.Length == 0)
            {
                InAuctionLog(tokenId, owner, price, -1);
                return new byte[] { 0x11 };
            }
            Aircraft aircraft = (Aircraft)Helper.Deserialize(aircraftInfo);
            if (aircraft.owner != owner)
            {
                InAuctionLog(tokenId, owner, price, -1);
                return new byte[] { 0x12 };
            }

            aircraft.price = price;
            // 当前时间增加三天，单位为秒
            aircraft.auctionTime = Blockchain.GetHeader(Blockchain.GetHeight()).Timestamp + 3 * 24 * 60 * 60;

            byte[] aircraftData = Helper.Serialize(aircraft);
            Storage.Put(Storage.CurrentContext, tokenId.AsByteArray(), aircraftData);

            InAuctionLog(tokenId, owner, price, aircraft.auctionTime);

            return new byte[] { 0x01 };
        }

        // 修改飞机所属者
        // 可用于购买飞机
        // Failure 0x11 / 0x12 / 0x16
        // Success 0x01
        public static byte[] ChangeOwner(BigInteger tokenId, byte[] fromOwner, byte[] toOwner)
        {
            byte[] aircraftInfo = Storage.Get(Storage.CurrentContext, tokenId.AsByteArray());
            if (aircraftInfo.Length == 0)
            {
                ChangeOwnerLog(tokenId, fromOwner, toOwner);
                return new byte[] { 0x11 };
            }
            Aircraft aircraft = (Aircraft)Helper.Deserialize(aircraftInfo);
            if (aircraft.owner != fromOwner)
            {
                ChangeOwnerLog(tokenId, fromOwner, toOwner);
                return new byte[] { 0x12 };
            }
            BigInteger currentTimestamp = Blockchain.GetHeader(Blockchain.GetHeight()).Timestamp;
            if (aircraft.auctionTime < currentTimestamp)
            {
                ChangeOwnerLog(tokenId, fromOwner, toOwner);
                return new byte[] { 0x16 };
            }

            // 执行所属转移
            aircraft.owner = toOwner;
            aircraft.auctionTime = 1525104000; // 较早的时间戳,清除售卖中状态
            byte[] aircraftData = Helper.Serialize(aircraft);
            Storage.Put(Storage.CurrentContext, tokenId.AsByteArray(), aircraftData);

            ChangeOwnerLog(tokenId, fromOwner, toOwner);

            return new byte[] { 0x01 };
        }

        // 升级飞机
        // Failure 0x11 / 0x12 / 0x13
        // Success 0x01
        public static byte[] Upgrade(BigInteger tokenId, byte[] owner, int fromLevel)
        {
            byte[] aircraftInfo = Storage.Get(Storage.CurrentContext, tokenId.AsByteArray());
            if (aircraftInfo.Length == 0)
            {
                UpgradeLevelLog(tokenId, owner, fromLevel);
                return new byte[] { 0x11 };
            }
            Aircraft aircraft = (Aircraft)Helper.Deserialize(aircraftInfo);
            if (aircraft.owner != owner)
            {
                UpgradeLevelLog(tokenId, owner, fromLevel);
                return new byte[] { 0x12 };
            }
            if (aircraft.level != fromLevel)
            {
                UpgradeLevelLog(tokenId, owner, fromLevel);
                return new byte[] { 0x13 };
            }
            BigInteger currentTimestamp = Blockchain.GetHeader(Blockchain.GetHeight()).Timestamp;
            if (aircraft.auctionTime > currentTimestamp)
            {
                UpgradeLevelLog(tokenId, owner, fromLevel);
                return new byte[] { 0x17 };
            }

            // 执行升级
            aircraft.level = fromLevel + 1;
            byte[] aircraftData = Helper.Serialize(aircraft);
            Storage.Put(Storage.CurrentContext, tokenId.AsByteArray(), aircraftData);

            UpgradeLevelLog(tokenId, owner, fromLevel + 1);

            return new byte[] { 0x01 };
        }

        public static byte[] OwnerOf(BigInteger tokenId)
        {
            byte[] aircraftInfo = Storage.Get(Storage.CurrentContext, tokenId.AsByteArray());
            if (aircraftInfo.Length == 0)
            {
                return new byte[] { 0x11 };
            }
            Aircraft aircraft = (Aircraft)Helper.Deserialize(aircraftInfo);
            return aircraft.owner;
        }

        public static Object TokenData(BigInteger tokenId)
        {
            byte[] aircraftInfo = Storage.Get(Storage.CurrentContext, tokenId.AsByteArray());
            if (aircraftInfo.Length == 0)
            {
                return new byte[] { 0x11 };
            }
            else
            {
                return (Aircraft)Helper.Deserialize(aircraftInfo);
            }
        }

        private static byte[] InitTcgAircraftWorld()
        {
            // 系统初始化
            return new byte[] { 0x01 };
        }

        // 系统产出飞机,系统创建的飞机初始只属于系统账号
        // 此处的prefix,不包含正式token中的最后三位标识数量
        // Failure 0x14 / 0x15
        // Success 0x01
        private static byte[] InitAircraft(BigInteger tokenPrefix, byte[] ownerId)
        {
            BigInteger tokenId = _getNextTokenId(tokenPrefix);
            if(tokenId == -1)
            {
                // 没有库存了，不能创建该颜色类型飞机
                BirthedLog(-1, ownerId);
                return new byte[] { 0x14 };
            }
            byte[] tokenIdKey = tokenId.AsByteArray();
            var existingAircraft = Storage.Get(Storage.CurrentContext, tokenIdKey);
            if (existingAircraft.Length == 0)
            {
                Aircraft newAircraft = new Aircraft();
                newAircraft.Id = tokenId;
                newAircraft.owner = ownerId;
                newAircraft.level = 1;
                newAircraft.auctionTime = 1525104000; // 较早的时间戳
                newAircraft.price = 100;

                byte[] aircraftData = Helper.Serialize(newAircraft);
                Storage.Put(Storage.CurrentContext, tokenIdKey, aircraftData);

                BirthedLog(tokenId, ownerId);
                return new byte[] { 0x01 };
            }
            else
            {
                BirthedLog(-1, ownerId);
                return new byte[] { 0x15 };
            }
        }

        // 根据prefix信息，查询当前index值，拼接新token完整id
        private static BigInteger _getNextTokenId(BigInteger tokenPrefix)
        {
            // tokenPrefix为14位随机结果
            // tokenSuffix为该类飞机的序号，该序号的第一位数字标识颜色，颜色分类用以控制飞机的数量
            // 序号的唯一性是针对颜色分开管理
            BigInteger color = tokenPrefix / 10000000000000;
            string aircraftSupplyStorageKey = _getAircraftSupplyStorageKey(color);
            byte[] currentTokenIndex = Storage.Get(Storage.CurrentContext, aircraftSupplyStorageKey);
            BigInteger newTokenIndex = 0;
            if (currentTokenIndex.Length != 0)
            {
                
                BigInteger colorLimit = CountLimit(color);
                if (colorLimit > currentTokenIndex.AsBigInteger())
                {
                    // 有剩余库存，可以继续创建
                    newTokenIndex = currentTokenIndex.AsBigInteger() + 1;
                }
                else
                {
                    return -1;
                }
            }
            else
            {
                newTokenIndex = 1;
            }

            Storage.Put(Storage.CurrentContext, aircraftSupplyStorageKey, newTokenIndex.AsByteArray());
            return tokenPrefix * 100000 + newTokenIndex;
        }

        private static string _getAircraftSupplyStorageKey(BigInteger colorPrefix)
        {
            return tokenStoragePrefix + colorPrefix;
        }
    }
}
