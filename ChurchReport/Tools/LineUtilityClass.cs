using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

#region CRM 2011 reference
using Microsoft.Xrm.Sdk;
using ToolUtility;
using System.Threading.Tasks;
#endregion

using Line.Messaging;
using System.IO;
using ToolUtilityNameSpace;

namespace ChurchReport.Tools
{
        public class LineUtilityClass
        {
            #region 系統參數
            //IServiceProvider m_ServiceProvider;
            //ITracingService m_TracingService;
            //IPluginExecutionContext m_Context;
            //
            //IOrganizationServiceFactory m_ServiceFactory;
            IOrganizationService m_CrmService;

            // 系統傳來的組織名稱
            public String m_OrganizationName = "";

            ReplyUtility m_ReplyUtility;

            #region Channel Access Token 設定

            // 客製化
            // 理債一日便的 Channel Access Token
            //private const String LINEMESSAGE_CHANNEL_ACCESS_TOKEN = @"0NhRlPIi85qb3pfJbhcyP+Y4Tw+F/Jz0kjHqzfvduTtdzlNOf9NJQW8DZ2NXpEWmpGYvEUQwekGNaoGtwKlu3+ugco6lu8QNGs1P14YeFRG3OSuXktpRt7atnYqMEl7ABYxgBSCq52pMVx58F/RpzwdB04t89/1O/w1cDnyilFU=";

            // 順風美醫診所 Channel Access Token
            private const String LINEMESSAGE_CHANNEL_ACCESS_TOKEN = @"s+583b2Rgbv4APgXhkNVpmx+wlaU04wWh82c/6i5Tyjsqh6SBQdBUjLc3b9C9tk4XK+1/TOeetLqFR+KdNromuUaS1Ih/T7gfXS3U/IRY0XqiQCYhrOC0TYKjeFuiDhAHpGidPcimIb6oVkqo5jBDQdB04t89/1O/w1cDnyilFU=";

            // 楊梅靈糧堂的 Channel Access Token
            //private const String YANGMEILLC_CHANNEL_ACCESS_TOKEN = @"RvnT/SCXqbHbGKSUm6y7PDW4G+KHMcsJPZdXqnEPg9JZiPrRcrYnn8jG/hn/Mvcher+IqARAc4B02aRzXCjrs+cI/VV7Gw2c3MsbhGlTJRSZntVfJeiKWejJqPT27dnstPcgaFER2FaW5sf9ipliQAdB04t89/1O/w1cDnyilFU=";
            //private const String YANGMEILLC_CHANNEL_ACCESS_TOKEN = @"KqGzRz3bTY+1uTNDICC++oZaGVrFu/L6coW217Sa0RBEapXMai2PKy9znYllvjyzq+XatskYzOrzhEcZgxRYC66YDrDdfr/BVaVwJDwtnCUnfKr6SV+M+OeUfyiDIlFxMPcxujF4/AWZLRiKpdTQgwdB04t89/1O/w1cDnyilFU=";
            //private const String YANGMEILLC_CHANNEL_ACCESS_TOKEN = @"s+583b2Rgbv4APgXhkNVpmx+wlaU04wWh82c/6i5Tyjsqh6SBQdBUjLc3b9C9tk4XK+1/TOeetLqFR+KdNromuUaS1Ih/T7gfXS3U/IRY0XqiQCYhrOC0TYKjeFuiDhAHpGidPcimIb6oVkqo5jBDQdB04t89/1O/w1cDnyilFU=";

            // 台北基督之家-50人試用版
            //private const String TPEHOC_CHANNEL_ACCESS_TOKEN = @"/iNy46gPp/ZXokg1Vr9RV/ZjodE3i7Q2o+k9nlH7l3pV8WzjAegGDduZc7gms8X5zrjSrDy2xSdNFud7JqjSDjwcTXZ6MJ/FF3NuhVg6WuXmMT34gAO7VZ0RWYrHXwAifVKpOyh2/8LiGgBpfo4ZXQdB04t89/1O/w1cDnyilFU=";
            //private const String TPEHOC_CHANNEL_ACCESS_TOKEN = @"Qw8a8etsFBpTYxhNtS7+kBqQpsdoJdw6Z70wI3Yv2XzBPfae5vz4A+wtfqmekAYWbsS+Aeg11OMRqPZOlgpzk0MGmS/wcFaNvr9n9cYl3Wt/XexnQmcbJkpE9peXa0ObnyV9nvbM5xdJGFSUl5WElwdB04t89/1O/w1cDnyilFU=";
            // 台北基督之家-線上付費版
            private const String TPEHOC_CHANNEL_ACCESS_TOKEN = @"MW7xRUVOMqzX651Akvg2cI8Z8oaX61lPAyL3QdSA94/pD61/FmU0wxj8rJ3CBp6Kle1qoDGIPXnMQuV5fhtYLELP+3nfPPiTdvvud9wrDp0uB204ovkDM3CE6wKpcpS2RUILadDWc4FXX6e8lyr+HQdB04t89/1O/w1cDnyilFU=";

            // 台北基督之家(公司內部開發測試)的 Channel Access Token
            //private const String TPEHOCBACK_CHANNEL_ACCESS_TOKEN = @"/iNy46gPp/ZXokg1Vr9RV/ZjodE3i7Q2o+k9nlH7l3pV8WzjAegGDduZc7gms8X5zrjSrDy2xSdNFud7JqjSDjwcTXZ6MJ/FF3NuhVg6WuXmMT34gAO7VZ0RWYrHXwAifVKpOyh2/8LiGgBpfo4ZXQdB04t89/1O/w1cDnyilFU=";
            private const String TPEHOCBACK_CHANNEL_ACCESS_TOKEN = @"Qw8a8etsFBpTYxhNtS7+kBqQpsdoJdw6Z70wI3Yv2XzBPfae5vz4A+wtfqmekAYWbsS+Aeg11OMRqPZOlgpzk0MGmS/wcFaNvr9n9cYl3Wt/XexnQmcbJkpE9peXa0ObnyV9nvbM5xdJGFSUl5WElwdB04t89/1O/w1cDnyilFU=";

            //台中生命之道靈糧堂
            //private const String YANGMEILLC_CHANNEL_ACCESS_TOKEN = @"YTd17Eep3V5/nSaI1lxLW5vx//gOfVr21kpnpZ6RBOfvFrjhJYpvtmCIy7yxDi2tQ2cfP/6qGJ9raS72VwN7xhGjneynJHpCRrgJbz4GqMGMMEjLAcVB+hRRNCTNkMOY3rYyyN/W+/sTAx3HzzhsPgdB04t89/1O/w1cDnyilFU=";

            //樹林教會
            //private const String YANGMEILLC_CHANNEL_ACCESS_TOKEN = @"36PV/e/hoJ9+CAqRwzO34PRWTQJSmkkIH0uXrV0bFPOSYmvUpNa1xx0G+BKrDmoce77OdGsItv4dTaLY35iG+KiIYpmkOzklQWm4N6jedvJKj9ruarXG+JKpPzUY6UlS0I+NS+6iD5ahJ+UhNaYaMwdB04t89/1O/w1cDnyilFU=";

            // 信友堂 Channel Access Token
            //private const String YANGMEILLC_CHANNEL_ACCESS_TOKEN = @"s+583b2Rgbv4APgXhkNVpmx+wlaU04wWh82c/6i5Tyjsqh6SBQdBUjLc3b9C9tk4XK+1/TOeetLqFR+KdNromuUaS1Ih/T7gfXS3U/IRY0XqiQCYhrOC0TYKjeFuiDhAHpGidPcimIb6oVkqo5jBDQdB04t89/1O/w1cDnyilFU=";

            //南崁長老教會
            //private const String NANKANCHURCH_CHANNEL_ACCESS_TOKEN = @"YTd17Eep3V5/nSaI1lxLW5vx//gOfVr21kpnpZ6RBOfvFrjhJYpvtmCIy7yxDi2tQ2cfP/6qGJ9raS72VwN7xhGjneynJHpCRrgJbz4GqMGMMEjLAcVB+hRRNCTNkMOY3rYyyN/W+/sTAx3HzzhsPgdB04t89/1O/w1cDnyilFU=";
            private const String NANKANCHURCH_CHANNEL_ACCESS_TOKEN = @"C9is9FmFfwQee0cauLjiBMwGVCfZSBfLespyMEVRYUUL98Fc3Mt/QgsIHNGFvkZX3rV6w+rlsejSeOKq6c14h6U5LVUY0m/vP8QnyzofBjeQ+zbwLzgNpbjSO2trrvUBRcJ6F9crM561EOsBPwusfgdB04t89/1O/w1cDnyilFU=";

            //大里思恩堂
            private const String DALI_CHANNEL_ACCESS_TOKEN = @"isUmSTPFmtTI0f5L4y/v0Ppl18HNXSB29Yu6X/1Okpnnlh07yzLJJvMb2PDWs3J/MzzrRwNwPTh/6bAZW2TqIcOhrWefFCnju2JraI1PvVogGcbO22tBZ1/vS1yZY/lw2z82QiXd63nhwW/Jyjn08gdB04t89/1O/w1cDnyilFU=";

            // 新店活水泉靈糧堂
            // 資料庫後臺用楊梅靈糧堂的，但是LINE 訊息用自己的試用版
            //private const String YANGMEILLC_CHANNEL_ACCESS_TOKEN = @"rHokvudlaAUKDRGeu31vkh5Aq/bO5n9H1jr0Xn5ynowF3cSFGr6oMnFoJ+RRc0NbL9wnUiGngyqDK23pubRJ1jXk86E0uFq6YKn3trFHFrvfWzz8shr71cQdWvZZH2OkW+xJdxl/mBya5kIFbDG02AdB04t89/1O/w1cDnyilFU=";

            // 中和喜樂城靈糧堂
            // 資料庫後臺用楊梅靈糧堂的，但是LINE 訊息用自己的試用版
            private const String YANGMEILLC_CHANNEL_ACCESS_TOKEN = @"GdR6j6eh0zRtEhUIJAhfRh3whD57VurJPC0ugYwhuhsIAWD+fDnMIiJtVI9T/KSz2ciq72k3PxP+w4Qs5uVTRzZPbftBBjyLiuLV+TKlLj+gdEq//Od7xYMXDEChs0LRRoGL5QL/vxcUZXiAZ4/cQQdB04t89/1O/w1cDnyilFU=";

            // 財團法人高雄市基督教會錫安堂
            //private const String KSZIONCHBACK_CHANNEL_ACCESS_TOKEN = @"4pOXVK9ujHBk/6wHN18lbsAb9usDmz3/w2WV7W16rH5OUB3XtUybWIMB7GWClWNfy7NU6o3kDCQkBDNJUfaDgCgnQAgXtspJoMBwXCOxxdD259QGPCTdlBYDilVn2x6fPqLxkouD2e2m8cbTEa6kbgdB04t89/1O/w1cDnyilFU=";
            private const String KSZIONCHBACK_CHANNEL_ACCESS_TOKEN = @"Bkqr0AA0fi04qf+5r35uRP+l9B/FcyKtYpOUowCvJnAoYoQuz5HMQbXC/kz3oNox+e3soYNcqCuTWY1R/e/w/ldkZ7+Pu5QslpKUlhATFD7y1h6or2Dbz7Wv8r1pkqcJ6Xn6S0wTwK8fVXU11Ywi7QdB04t89/1O/w1cDnyilFU=";
            private const String KSZIONCH_CHANNEL_ACCESS_TOKEN = @"Bkqr0AA0fi04qf+5r35uRP+l9B/FcyKtYpOUowCvJnAoYoQuz5HMQbXC/kz3oNox+e3soYNcqCuTWY1R/e/w/ldkZ7+Pu5QslpKUlhATFD7y1h6or2Dbz7Wv8r1pkqcJ6Xn6S0wTwK8fVXU11Ywi7QdB04t89/1O/w1cDnyilFU=";

            // 思恩堂豐富教會(50人版)
            //private const String ABUNDANCE_CHANNEL_ACCESS_TOKEN = @"PhC1ibjhqnR1CiDPyRsO6yvTmB1pWRiZAEQEsdTc0ibRd9hn3j1u3yOZf6IFneDsy3x1TBJgL1ODRxhpm9nTjELXi6uK3NFBapHXlogGsZryEIq6rZAVQ37cwquPr6sruwmkvRjQrxIvubS50aXBEwdB04t89/1O/w1cDnyilFU=";
            // 思恩堂豐富教會(付費版)
            private const String ABUNDANCE_CHANNEL_ACCESS_TOKEN = @"yvyzlpbDY4ctjVuC0vEYFDF4Gz9Ed6VR57AOmqEfRPqNSFa4tmlvgFqydqOsv8C5vOG3Ew1vPtBfZoJ7Psm69HH+oKtRA4UeMWi1EZp6j4hzhjC1ePmBRQOdfcbcGgDjJzC60Q8HAI/Err6YjFZwOwdB04t89/1O/w1cDnyilFU=";

            // 思恩堂豐富教會-公司內部研發(50人版)
            private const String ABUNDANCE_BACK_CHANNEL_ACCESS_TOKEN = "k4/gFG2xonyaewMi8NIPgYdqpcIDnixEpemNIEswwFPzltmlm2kGB6i+uuvvmBaxg9l8wXympy37Y2h7ueq6ECUhTGyBovUXyqgH6lF6aa5R757vsN7sRX7o03dx7tPbj5J5dICcR1JRbvBvxvZ3KQdB04t89/1O/w1cDnyilFU=";

            // 台中生命之道靈糧堂-雲端(50人版)
            //private const String WOL_CHANNEL_ACCESS_TOKEN = @"XofqB1wMctFHMnfJVkxGIivnfXKrRYyOzKSLrk2JtlGJz9o/esnnUf5dX4y8TRNBIrMsrEwm0Z38Zb3IISBzeokAtMD8B8oYFCtaqeeRj7RYqvBU8kDSIe6ECx+6DQfceAECSSO4vaHc+QSqeOLk4AdB04t89/1O/w1cDnyilFU=";
            // 台中生命之道靈糧堂-雲端 Line 2.0
            private const String WOL_CHANNEL_ACCESS_TOKEN = @"ipt4NYZUDb6bgjBbrqkJN+WPAa9cQwXbv/++eittHwauIlj1lokJjUAS00lWjdmX63BiH+J9WCq7jmf0Bw6cXlsgIeIh2xYeZ8K8OxpCVICT1c6yWEFOubQpthNTeY06xnXFsVZPGP2GevRXshviDAdB04t89/1O/w1cDnyilFU=";

            // 中和喜樂城靈糧堂-公司內部研發(50人版)
            private const String JOYTOWNBACK_CHANNEL_ACCESS_TOKEN = @"aMTPQvXn6Cp+DLx6dqEbVp2hX5kaXdHb1fW4jrltl1+2jm8iZGstG6rvwbHpunL4gS5dS0uRgMVDtnsZvYSy4WKqglQZTEQSRBIXfAHl/S/cMYFkUfRtvnS1r9kStKkG3HxDjG+i1dH5N5UxlZlN9AdB04t89/1O/w1cDnyilFU=";
            // 中和喜樂城靈糧堂-雲端(進階付費版)
            private const String JOYTOWN_CHANNEL_ACCESS_TOKEN = @"MZ/feT/qyNmSBQ+WX5PrlMXZcr+YwowhKekhx+s7gUE8UpWMcIqChSWrPf9Go7Q72iFkH4iru/8ebVXGChjG8gMHR5V6kpKtlasPPHATV8k/j2mS+dPSrVBVXs2e4t9zrr8dcJVVH+9Xq067WbP3GwdB04t89/1O/w1cDnyilFU=";

            // 博世牙醫-公司內部研發(50人版)
            private const String PRODENT_CHANNEL_ACCESS_TOKEN = "dmbuR5K74MeLUULRFcPpfHRpo254+J5006MXv7iBoRr8apSW+rA3IiJm4fbSrif4baqx91W4Q68UKpzvrtCHmPcdgy3G7x+lNwuwnN6UgJWox63Caqf/Wu0ifj6grPOg8fv0cHOLw4gru8SVKvLdLwdB04t89/1O/w1cDnyilFU=";

            // 高雄基督之家-公司內部研發(進階付費版)
            private const String KHHOCBACK_CHANNEL_ACCESS_TOKEN = "a5bB4sunKwoZGjbf0HvFnenCpiABmzIT6rGU4rQ25QAqDhxj8Wa+RwXKQN2CZVC3lSk2sZ2n5bqzCcvaa8J/DIOzUdLUUgq1wF6SIvcd0sL0uFWn0+XyaQXdii1QHvA4Lm+NU5wehU4zIhdxZaMMsAdB04t89/1O/w1cDnyilFU=";
            // 高雄基督之家-雲端(進階付費版)
            private const String KHHOC_CHANNEL_ACCESS_TOKEN = "a5bB4sunKwoZGjbf0HvFnenCpiABmzIT6rGU4rQ25QAqDhxj8Wa+RwXKQN2CZVC3lSk2sZ2n5bqzCcvaa8J/DIOzUdLUUgq1wF6SIvcd0sL0uFWn0+XyaQXdii1QHvA4Lm+NU5wehU4zIhdxZaMMsAdB04t89/1O/w1cDnyilFU=";

            // 我自己的音訊靈糧堂展示用
            private const String CHURCH_CHANNEL_ACCESS_TOKEN = "olB/lJ55plRTngOA8I2h6U6zAXyS6xOVAM/xX0NvY/8BDYLujS0rqaPaBnRyGFyLUVAbIullsxEFN86CYVzUHCqQyMiF2wlmnPx7znO46yYUByEjL0mVXlaYHeYHD8WDYzW39NLr2UBHIS9q1q1gSwdB04t89/1O/w1cDnyilFU=";

            // 台中慕義堂展示用
            //private const String CHURCH_CHANNEL_ACCESS_TOKEN = "WDWmhzbDlQNgqeAP6vuGbQB53Qy9rUwSLKTdtcfAW34HgH5l1oEGBnFJAMx/U2n2/n2Wa9SbUXDx7WIR5g+/HX1goTNMUJvDWmhP8v6fcFijOnqXPQ3VWef87IFN9i5k+RRHET70B0Njkq6CoM7zoAdB04t89/1O/w1cDnyilFU=";

            // 宜蘭靈糧堂(研發50人免費版)
            //private const String LYLLCILAN_CHANNEL_ACCESS_TOKEN = "wVw4DNGyg93il9ARut9Ir3vOtr5/814S56vAGpLbLfd39fzdQo6n6gm2RMwF9SDGYiDB7X4AUmNW/+NdFOGSjTRaU8GzKATVGrVXPg8EsMlD0gjTUm8Ij2A1WCtAg8Kt+O6DMmyL8P4abBegWgNlfgdB04t89/1O/w1cDnyilFU=";
            // 宜蘭靈糧堂(Line 2.0)
            private const String LYLLCILAN_CHANNEL_ACCESS_TOKEN = "B30SUUpOHKcguMVXPxm7LhoHybHPMawCqVwG/uw1H/2p3suuDG02qIVQG32aPpGyFJDmIZFcsFaWB2ps0LHwvgHr5dOZCMMWTjnhAD6GaTHFozI7kEN+2loJfI1qNUl0m4WKt6jvGPQGetOWv7sbdwdB04t89/1O/w1cDnyilFU=";

            // 城市之光聖教會(研發50人免費版)
            //private const String CLHC_CHANNEL_ACCESS_TOKEN = "1i8xFY0BlhjzLXAht063rW+zAhdM5uWN/jAhssYMq+10+VqENL4LxwLQQHhgSKotgYp3r/lgdL/kvUf/o7cWWyzn0ZftvExbiEEnnR7KEfmFdAcjFT/DCBvnD2c5bjwTxWxE//H+ZNGkSdmzWKliVwdB04t89/1O/w1cDnyilFU=";
            // 城市之光聖教會(Line 2.0)
            private const String CLHC_CHANNEL_ACCESS_TOKEN = "zUGKNYbCG42sZUwSVv9l2m4T0Mcb8oIK4n9O+gMPUl1kpEGoUNFCrR4A4YKVFwgFFvhKFwi47HMQpIiwI6GC4rm+Nk2wa7dnvNnPoRL5/oqCgRgWUBBwzv7r/8WL1PsBe/ZHjTnhDmodvAEBBydXjgdB04t89/1O/w1cDnyilFU=";

            // 音訊教會-雲端展示 (50人版)
            //private const String CHURCH_CHANNEL_ACCESS_TOKEN = "7/Q4Iw9Z71fjo5nGtIdsCFtuJLR+2gGOOSKlGEi3rxYI9PsUHKnjjn0D4DtQN6PWfYaRr+/aUIT42Eg3LLFej7sSXopZcuuntN/bCMsDS6Eszbqcv/jodqYCVNX0iWhoqk8nLtTxH+CuWt4kmFgJmAdB04t89/1O/w1cDnyilFU=";
            private const String JESUS_CHANNEL_ACCESS_TOKEN = "g1jtWWNkjbH3OCh1cKoRvPBUkCJIygNuvV/neHXR9I4J5GBgVE85inaIaTcT4AAZ1qCuqrqJXDawrUweyBqLcX97GGokXnTRQ6MxjXAutd5Yr2FkPsZnq6kMelc/C+mqNUHaVUKFAuvTD8JvXbNmpAdB04t89/1O/w1cDnyilFU=";

            // 楊梅靈糧堂 Line 2.0
            //private const String CHURCH_CHANNEL_ACCESS_TOKEN = "7/Q4Iw9Z71fjo5nGtIdsCFtuJLR+2gGOOSKlGEi3rxYI9PsUHKnjjn0D4DtQN6PWfYaRr+/aUIT42Eg3LLFej7sSXopZcuuntN/bCMsDS6Eszbqcv/jodqYCVNX0iWhoqk8nLtTxH+CuWt4kmFgJmAdB04t89/1O/w1cDnyilFU=";
            private const String YMLLCBACK_CHANNEL_ACCESS_TOKEN = "VrrLlxYzHXBTIWg+dK3zfSStpjaKq+I4CtIMzHvl1DRKlPtvNQuIGafYkna6Am2Eic2lR5/mR6D4XatoGnFQrs6nWaZDEkMWBXycxkpNP5SSvIm11brm0yA/E8EHFJCA7zY66wmrD8jzJ0xNRMmy9wdB04t89/1O/w1cDnyilFU=";
            private const String YMLLC_CHANNEL_ACCESS_TOKEN = "VrrLlxYzHXBTIWg+dK3zfSStpjaKq+I4CtIMzHvl1DRKlPtvNQuIGafYkna6Am2Eic2lR5/mR6D4XatoGnFQrs6nWaZDEkMWBXycxkpNP5SSvIm11brm0yA/E8EHFJCA7zY66wmrD8jzJ0xNRMmy9wdB04t89/1O/w1cDnyilFU=";

            // 台中思恩堂 Line 2.0
            //private const String CHURCH_CHANNEL_ACCESS_TOKEN = "7/Q4Iw9Z71fjo5nGtIdsCFtuJLR+2gGOOSKlGEi3rxYI9PsUHKnjjn0D4DtQN6PWfYaRr+/aUIT42Eg3LLFej7sSXopZcuuntN/bCMsDS6Eszbqcv/jodqYCVNX0iWhoqk8nLtTxH+CuWt4kmFgJmAdB04t89/1O/w1cDnyilFU=";
            private const String GRACEBACK_CHANNEL_ACCESS_TOKEN = "qlTCgzJwvW5GUOlbLxyE5+KPTErEinaMI82mhUWmEzb76MHFDTMWCMrK28LpYRAl8tQH/ygaOWJ7ZaG+bEVlSv8iP4L4qPcrZ/2j85bb2uc5H37/0Ikm070LpWcJqgScfTr3ANWlqq3Us2rTUF6gvwdB04t89/1O/w1cDnyilFU=";
            private const String GRACE_CHANNEL_ACCESS_TOKEN = "qlTCgzJwvW5GUOlbLxyE5+KPTErEinaMI82mhUWmEzb76MHFDTMWCMrK28LpYRAl8tQH/ygaOWJ7ZaG+bEVlSv8iP4L4qPcrZ/2j85bb2uc5H37/0Ikm070LpWcJqgScfTr3ANWlqq3Us2rTUF6gvwdB04t89/1O/w1cDnyilFU=";

            // 台中忠孝路長老教會 Line 2.0
            //private const String CHURCH_CHANNEL_ACCESS_TOKEN = "7/Q4Iw9Z71fjo5nGtIdsCFtuJLR+2gGOOSKlGEi3rxYI9PsUHKnjjn0D4DtQN6PWfYaRr+/aUIT42Eg3LLFej7sSXopZcuuntN/bCMsDS6Eszbqcv/jodqYCVNX0iWhoqk8nLtTxH+CuWt4kmFgJmAdB04t89/1O/w1cDnyilFU=";
            private const String CHUNG_HSIAO_BACK_CHANNEL_ACCESS_TOKEN = "aKS4zYeq2ZpqlLd4gslkWAyYuiC+B2f1noatF1VylPvkR2+mrvJ7mwnIIXtn2Pi117NBmNTmRZL5DO5ZMYaGCj/v9+fB6Zn9sel42Jr55PlegJdrtoSvPgm4fBso1tY/7H65+cOFDQxjqhdOU69qQAdB04t89/1O/w1cDnyilFU=";
            private const String CHUNG_HSIAO_CHANNEL_ACCESS_TOKEN = "aKS4zYeq2ZpqlLd4gslkWAyYuiC+B2f1noatF1VylPvkR2+mrvJ7mwnIIXtn2Pi117NBmNTmRZL5DO5ZMYaGCj/v9+fB6Zn9sel42Jr55PlegJdrtoSvPgm4fBso1tY/7H65+cOFDQxjqhdOU69qQAdB04t89/1O/w1cDnyilFU=";

            // 永和禮拜堂 Line 2.0
            //private const String CHURCH_CHANNEL_ACCESS_TOKEN = "7/Q4Iw9Z71fjo5nGtIdsCFtuJLR+2gGOOSKlGEi3rxYI9PsUHKnjjn0D4DtQN6PWfYaRr+/aUIT42Eg3LLFej7sSXopZcuuntN/bCMsDS6Eszbqcv/jodqYCVNX0iWhoqk8nLtTxH+CuWt4kmFgJmAdB04t89/1O/w1cDnyilFU=";
            private const String YH_CHURCH_CHANNEL_ACCESS_TOKEN = "HeuLkSEF5CX7hdZo4956IPpgJNdb8VqRZeL1Gu37kFFm+1F7DObAGjfeVYaggzwjZ5H4qraesvquODt7Y81jbtspNZkEq5n3oLDG+G32xQsRx1jCobkABL/Z7RKjkSACNT6h72bPQXsVn9aCuI5OogdB04t89/1O/w1cDnyilFU=";
            private const String YH_CHURCH_BACK_CHANNEL_ACCESS_TOKEN = "HeuLkSEF5CX7hdZo4956IPpgJNdb8VqRZeL1Gu37kFFm+1F7DObAGjfeVYaggzwjZ5H4qraesvquODt7Y81jbtspNZkEq5n3oLDG+G32xQsRx1jCobkABL/Z7RKjkSACNT6h72bPQXsVn9aCuI5OogdB04t89/1O/w1cDnyilFU=";

            // 慕義堂 Line 2.0
            //private const String CHURCH_CHANNEL_ACCESS_TOKEN = "7/Q4Iw9Z71fjo5nGtIdsCFtuJLR+2gGOOSKlGEi3rxYI9PsUHKnjjn0D4DtQN6PWfYaRr+/aUIT42Eg3LLFej7sSXopZcuuntN/bCMsDS6Eszbqcv/jodqYCVNX0iWhoqk8nLtTxH+CuWt4kmFgJmAdB04t89/1O/w1cDnyilFU=";
            private const String MUYI_CHANNEL_ACCESS_TOKEN = "WDWmhzbDlQNgqeAP6vuGbQB53Qy9rUwSLKTdtcfAW34HgH5l1oEGBnFJAMx/U2n2/n2Wa9SbUXDx7WIR5g+/HX1goTNMUJvDWmhP8v6fcFijOnqXPQ3VWef87IFN9i5k+RRHET70B0Njkq6CoM7zoAdB04t89/1O/w1cDnyilFU=";
            private const String MUYI_BACK_CHANNEL_ACCESS_TOKEN = "WDWmhzbDlQNgqeAP6vuGbQB53Qy9rUwSLKTdtcfAW34HgH5l1oEGBnFJAMx/U2n2/n2Wa9SbUXDx7WIR5g+/HX1goTNMUJvDWmhP8v6fcFijOnqXPQ3VWef87IFN9i5k+RRHET70B0Njkq6CoM7zoAdB04t89/1O/w1cDnyilFU=";

            // iM行動教會 Line 2.0
            private const String IM_CHURCH_CHANNEL_ACCESS_TOKEN = "XwSRWX0RxTtTvY/N6QZQ9YElOMH3OAxBf/3DAmWoXbIK3ymBsXEaU54owfdbPTQiQJPd10cWjC+JIWX6EvOCTbBdHmmJNC6xOOaioB91gPJPyDpl0IHQOQAzLA9J21zZ83SgIF6JwJbxC/8tSXv6RgdB04t89/1O/w1cDnyilFU=";
            private const String IM_CHURCH_BACK_CHANNEL_ACCESS_TOKEN = "YJ1LKtDZyfHwfkbqeHAk+pxNJNZBpOvI446h3brWHDqquFc2ElUCYaseqiW+pAKhwJspguAgGbOlKDymSjSTMydJn7JeY6CRmeyC2Am7urM3CNVNq/2JzAuQ2Vqc7lhPWx8qX5YxS3ve4NjcDceymQdB04t89/1O/w1cDnyilFU=";

            // 安平禮拜堂 Line 2.0
            private const String RPG_CHANNEL_ACCESS_TOKEN = "MwTnnrBtGgUaj+ZfbiKx7dxYxIuJKBmX9PLwKcRQU+VG4u0Gvyv2VeIjmNOr3pVGfH4JizB2wNbT0K0c4pT/XXCoBpK3lMQGaRAfS0FMoy05WDFQJgTL7etz9BHrzzWL6j0aFfutv6F4sMvcAdkTPgdB04t89/1O/w1cDnyilFU=";
            private const String RPG_BACK_CHANNEL_ACCESS_TOKEN = "MwTnnrBtGgUaj+ZfbiKx7dxYxIuJKBmX9PLwKcRQU+VG4u0Gvyv2VeIjmNOr3pVGfH4JizB2wNbT0K0c4pT/XXCoBpK3lMQGaRAfS0FMoy05WDFQJgTL7etz9BHrzzWL6j0aFfutv6F4sMvcAdkTPgdB04t89/1O/w1cDnyilFU=";

            // 安平禮拜堂 Line 2.0
            private const String DHCHURCH_CHANNEL_ACCESS_TOKEN = "MwTnnrBtGgUaj+ZfbiKx7dxYxIuJKBmX9PLwKcRQU+VG4u0Gvyv2VeIjmNOr3pVGfH4JizB2wNbT0K0c4pT/XXCoBpK3lMQGaRAfS0FMoy05WDFQJgTL7etz9BHrzzWL6j0aFfutv6F4sMvcAdkTPgdB04t89/1O/w1cDnyilFU=";
            private const String DHCHURCH_BACK_CHANNEL_ACCESS_TOKEN = "MwTnnrBtGgUaj+ZfbiKx7dxYxIuJKBmX9PLwKcRQU+VG4u0Gvyv2VeIjmNOr3pVGfH4JizB2wNbT0K0c4pT/XXCoBpK3lMQGaRAfS0FMoy05WDFQJgTL7etz9BHrzzWL6j0aFfutv6F4sMvcAdkTPgdB04t89/1O/w1cDnyilFU=";
        #endregion

        String m_ChannelAccessToken = LINEMESSAGE_CHANNEL_ACCESS_TOKEN;

            LineMessagingClient m_LineMessagingClient;

            private const String WEB_LINK = @"http://www.speechmessage.com.tw";

            private const String DEVELOPER_LINE_ID = @"U7638e4ed509708a3573ba6d69970583d";

            // Line 選單圖形檔案位置
            private const String LINE_MENU_PATH = @"D:\Line 選單\";


            // 模板預設的圖片
            private const String m_Default_ThumbnailImageUrl = "https://web.opendrive.com/api/v1/download/file.json/ODdfMzk3Nzc5Nl8?inline=1";
            // 楊梅靈糧堂模板預設的圖片
            private const String m_Yangmeillc_ThumbnailImageUrl = "https://web.opendrive.com/api/v1/download/file.json/ODdfMzk3Nzc5Nl8?inline=1";
            // 台北基督之家模板預設的圖片
            private const String m_TpeHoc_ThumbnailImageUrl = "https://od.lk/s/ODdfNTg5ODc5OF8/2017_06_sermon_6-18.jpg";

            #endregion

            #region 釋放記憶體
            private bool _disposed = false;

            protected virtual void Dispose(bool disposing)
            {
                if (_disposed) return;

                if (disposing)
                {
                    m_ToolUtilityClass.Dispose();
                }

                _disposed = true;
            }

            public void Dispose()
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }

            ~LineUtilityClass()
            {
                // Do not re-create Dispose clean-up code here.
                // Calling Dispose(false) is optimal in terms of
                // readability and maintainability.
                Dispose(false);
            }
            #endregion

            ToolUtilityClass m_ToolUtilityClass;

            public LineUtilityClass( ToolUtilityClass aToolUtilityClass)
            {
                m_LineMessagingClient = new LineMessagingClient(m_ChannelAccessToken);

                m_ReplyUtility = new ReplyUtility(m_LineMessagingClient);
            }

            public void SetupChannelAccessToken(ref IOrganizationService aCrmService)
            {
                try
                {
                    // 客製化，請選擇
                    // 先取得組織名稱
                    if (this.m_OrganizationName == "linemessage")
                    {
                        m_ChannelAccessToken = LINEMESSAGE_CHANNEL_ACCESS_TOKEN;
                    }
                    else if (this.m_OrganizationName == "yangmeillc")
                    {
                        m_ChannelAccessToken = YANGMEILLC_CHANNEL_ACCESS_TOKEN;
                    }
                    else if (this.m_OrganizationName == "tpehoc")
                    {
                        m_ChannelAccessToken = TPEHOC_CHANNEL_ACCESS_TOKEN;
                    }
                    else if (this.m_OrganizationName == "tpehocback")
                    {
                        m_ChannelAccessToken = TPEHOCBACK_CHANNEL_ACCESS_TOKEN;
                    }
                    else if (this.m_OrganizationName == "nankanchurchback")
                    {
                        m_ChannelAccessToken = NANKANCHURCH_CHANNEL_ACCESS_TOKEN;
                    }
                    else if (this.m_OrganizationName == "daliback")
                    {
                        m_ChannelAccessToken = DALI_CHANNEL_ACCESS_TOKEN;
                    }
                    else if (this.m_OrganizationName == "kszionchback")
                    {
                        m_ChannelAccessToken = KSZIONCHBACK_CHANNEL_ACCESS_TOKEN;
                    }
                    else if (this.m_OrganizationName == "kszionch")
                    {
                        m_ChannelAccessToken = KSZIONCH_CHANNEL_ACCESS_TOKEN;
                    }
                    else if (this.m_OrganizationName == "abundance")
                    {
                        m_ChannelAccessToken = ABUNDANCE_CHANNEL_ACCESS_TOKEN;
                    }
                    else if (this.m_OrganizationName == "abundanceback")
                    {
                        //m_ChannelAccessToken = ABUNDANCE_BACK_CHANNEL_ACCESS_TOKEN;

                        // 公司研發後台的慕義堂展示，先借用台中思恩堂豐富教會(公司研發)的資料庫
                        //m_ChannelAccessToken = MUYI_CHANNEL_ACCESS_TOKEN; 
                        m_ChannelAccessToken = ABUNDANCE_BACK_CHANNEL_ACCESS_TOKEN;
                    }
                    else if (this.m_OrganizationName == "wol")
                    {
                        m_ChannelAccessToken = WOL_CHANNEL_ACCESS_TOKEN;
                    }
                    else if (this.m_OrganizationName == "wolback")
                    {
                        m_ChannelAccessToken = WOL_CHANNEL_ACCESS_TOKEN;
                    }
                    else if (this.m_OrganizationName == "joytownback")
                    {
                        m_ChannelAccessToken = JOYTOWNBACK_CHANNEL_ACCESS_TOKEN;
                    }
                    else if (this.m_OrganizationName == "joytown")
                    {
                        m_ChannelAccessToken = JOYTOWN_CHANNEL_ACCESS_TOKEN;
                    }
                    else if (this.m_OrganizationName == "khhocback")
                    {
                        m_ChannelAccessToken = KHHOCBACK_CHANNEL_ACCESS_TOKEN;
                    }
                    else if (this.m_OrganizationName == "khhoc")
                    {
                        m_ChannelAccessToken = KHHOC_CHANNEL_ACCESS_TOKEN;
                    }
                    else if (this.m_OrganizationName == "church")
                    {
                        m_ChannelAccessToken = CHURCH_CHANNEL_ACCESS_TOKEN;
                    }
                    else if (this.m_OrganizationName == "jesus")
                    {
                        m_ChannelAccessToken = JESUS_CHANNEL_ACCESS_TOKEN;
                    }
                    else if (this.m_OrganizationName == "lyllcilanback")
                    {
                        m_ChannelAccessToken = LYLLCILAN_CHANNEL_ACCESS_TOKEN;
                    }
                    else if (this.m_OrganizationName == "lyllcilan")
                    {
                        m_ChannelAccessToken = LYLLCILAN_CHANNEL_ACCESS_TOKEN;
                    }
                    else if (this.m_OrganizationName == "clhcback")
                    {
                        m_ChannelAccessToken = CLHC_CHANNEL_ACCESS_TOKEN;
                    }
                    else if (this.m_OrganizationName == "clhc")
                    {
                        m_ChannelAccessToken = CLHC_CHANNEL_ACCESS_TOKEN;
                    }
                    else if (this.m_OrganizationName == "ymllcback")
                    {
                        m_ChannelAccessToken = YMLLCBACK_CHANNEL_ACCESS_TOKEN;
                    }
                    else if (this.m_OrganizationName == "ymllc")
                    {
                        m_ChannelAccessToken = YMLLC_CHANNEL_ACCESS_TOKEN;
                    }
                    else if (this.m_OrganizationName == "graceback")
                    {
                        m_ChannelAccessToken = GRACEBACK_CHANNEL_ACCESS_TOKEN;
                    }
                    else if (this.m_OrganizationName == "grace")
                    {
                        m_ChannelAccessToken = GRACE_CHANNEL_ACCESS_TOKEN;
                    }
                    else if (this.m_OrganizationName == "chunghsiaoback")
                    {
                        m_ChannelAccessToken = CHUNG_HSIAO_BACK_CHANNEL_ACCESS_TOKEN;
                    }
                    else if (this.m_OrganizationName == "chunghsiao")
                    {
                        m_ChannelAccessToken = CHUNG_HSIAO_CHANNEL_ACCESS_TOKEN;
                    }
                    else if (this.m_OrganizationName == "yhchurch")
                    {
                        m_ChannelAccessToken = YH_CHURCH_CHANNEL_ACCESS_TOKEN;
                    }
                    else if (this.m_OrganizationName == "yhchurchback")
                    {
                        m_ChannelAccessToken = YH_CHURCH_BACK_CHANNEL_ACCESS_TOKEN;
                    }
                    else if (this.m_OrganizationName == "imchurch")
                    {
                        m_ChannelAccessToken = IM_CHURCH_CHANNEL_ACCESS_TOKEN;
                    }
                    else if (this.m_OrganizationName == "imchurchback")
                    {
                        m_ChannelAccessToken = IM_CHURCH_BACK_CHANNEL_ACCESS_TOKEN;
                    }
                    else if (this.m_OrganizationName == "muyi")
                    {
                        m_ChannelAccessToken = MUYI_CHANNEL_ACCESS_TOKEN;
                    }
                    else if (this.m_OrganizationName == "muyiback")
                    {
                        m_ChannelAccessToken = MUYI_BACK_CHANNEL_ACCESS_TOKEN;
                    }
                    else if (this.m_OrganizationName == "rpg")
                    {
                        m_ChannelAccessToken = RPG_CHANNEL_ACCESS_TOKEN;
                    }
                    else if (this.m_OrganizationName == "rpgback")
                    {
                        m_ChannelAccessToken = RPG_BACK_CHANNEL_ACCESS_TOKEN;
                    }
                    else if (this.m_OrganizationName == "dhchurch")
                    {
                        m_ChannelAccessToken = DHCHURCH_CHANNEL_ACCESS_TOKEN;
                    }
                    else if (this.m_OrganizationName == "dhchurchback")
                    {
                        m_ChannelAccessToken = DHCHURCH_BACK_CHANNEL_ACCESS_TOKEN;
                    }
                    else
                    {
                    m_ChannelAccessToken = MUYI_CHANNEL_ACCESS_TOKEN;
                    }
                }
                catch (System.Exception e)
                {
                    String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                    throw e;
                }
            }

            #region 工具區
            #region Line Messagin Api SDK傳送
            public async Task ReplyMessage(string ReplyToken, List<ISendMessage> MessageToSend)
            {
                try
                {
                    await this.m_ReplyUtility.ReplyMessage(ReplyToken, MessageToSend);

                    return;
                }
                catch (System.Exception e)
                {
                    String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                }
            }
            public async Task ReplyTextMessage(string ReplyToken, string Message)
            {
                await this.m_ReplyUtility.ReplyMessageAsync(ReplyToken, Message);

                return;
            }
            public async Task SendMessage(string UserId, List<ISendMessage> MessageToSend)
            {
                await this.m_LineMessagingClient.PushMessageAsync(UserId, MessageToSend);

                return;
            }
            public async Task SendMessageAsync(string UserId, string Message)
            {
                this.m_ToolUtilityClass.CreatePushLineMessage(UserId, "Line推播統計:文字", Message);
                List<ISendMessage> MessageToSend = new List<ISendMessage>
                {
                    new TextMessage(Message)
                };

                await this.m_LineMessagingClient.PushMessageAsync(UserId, MessageToSend);

                //this.m_ToolUtilityClass.TraceByLevel(5, 1, "傳送結果=" + aHttpResponseMessage);

                return;
            }
            public async Task MultiCastTextMessageAsync(IList<string> To, string Message)
            {
                try
                {
                    List<ISendMessage> MessageToSend = new List<ISendMessage>
                    {
                        new TextMessage(Message)
                    };

                    await this.m_LineMessagingClient.MultiCastMessageAsync(To, MessageToSend);

                    return;
                }
                catch (System.Exception e)
                {
                    String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                    throw e;
                }

            }
            public void SendMessage(string UserId, string Message)
            {
                this.m_ToolUtilityClass.CreatePushLineMessage(UserId, "Line推播統計:文字", Message);
                List<ISendMessage> MessageToSend = new List<ISendMessage>
                {
                    new TextMessage(Message)
                };

                this.m_LineMessagingClient.PushMessageAsync(UserId, MessageToSend);

                return;
            }
            public async Task SendImage(string UserId, string OriginalContenUrl, string PreviewImageUrl)
            {
                this.m_ToolUtilityClass.CreatePushLineMessage(UserId, "Line推播統計:圖片", "");
                List<ISendMessage> MessageToSend = new List<ISendMessage>
                {
                    new ImageMessage(OriginalContenUrl, PreviewImageUrl)
                };

                await this.m_LineMessagingClient.PushMessageAsync(UserId, MessageToSend);

                return;
            }
            public async Task ReplyImage(string ReplyToken, string OriginalContenUrl, string PreviewImageUrl)
            {
                List<ISendMessage> MessageToSend = new List<ISendMessage>
                {
                    new ImageMessage(OriginalContenUrl, PreviewImageUrl)
                };

                await this.m_LineMessagingClient.ReplyMessageAsync(ReplyToken, MessageToSend);

                return;
            }
            public async Task SendVideo(string UserId, string OriginalContenUrl, string PreviewImageUrl)
            {
                this.m_ToolUtilityClass.CreatePushLineMessage(UserId, "Line推播統計:影片", "");
                List<ISendMessage> MessageToSend = new List<ISendMessage>
                {
                    new VideoMessage(OriginalContenUrl, PreviewImageUrl)
                };

                await this.m_LineMessagingClient.PushMessageAsync(UserId, MessageToSend);

                return;
            }
            public async Task SendAudeo(string UserId, string OriginalContenUrl, long Duration)
            {
                this.m_ToolUtilityClass.CreatePushLineMessage(UserId, "Line推播統計:聲音", "");
                List<ISendMessage> MessageToSend = new List<ISendMessage>
                {
                    new AudioMessage(OriginalContenUrl, Duration)
                };

                await this.m_LineMessagingClient.PushMessageAsync(UserId, MessageToSend);

                return;
            }
            public async Task SendLocation(string UserId, string Title, string Address, decimal Latitude, decimal Longitude)
            {
                this.m_ToolUtilityClass.CreatePushLineMessage(UserId, "Line推播統計:座標", "");
                List<ISendMessage> MessageToSend = new List<ISendMessage>
                {
                    new LocationMessage(Title, Address, Latitude, Longitude)
                };

                await this.m_LineMessagingClient.PushMessageAsync(UserId, MessageToSend);

                return;
            }
            public async Task SendSticker(string UserId, int PackageId, int StickerId)
            {
                this.m_ToolUtilityClass.CreatePushLineMessage(UserId, "Line推播統計:貼圖", "");
                List<ISendMessage> MessageToSend = new List<ISendMessage>
                {
                    new StickerMessage(PackageId.ToString(), StickerId.ToString())
                };

                await this.m_LineMessagingClient.PushMessageAsync(UserId, MessageToSend);

                return;
            }
            public async Task PostSerializedTemplate(Entity aLetterEntity, string UserId, String AltText, String ThumbnailImageUrl, String Title, String Text, List<ITemplateAction> aITemplateAction)
            {
                this.m_ToolUtilityClass.CreatePushLineMessage(UserId, "Line推播統計:Template", "");
                ISendMessage ButtonsTemplateMessage = new TemplateMessage
                    (
                        AltText,
                        new ButtonsTemplate
                        (
                            text: Text,
                            title: Title,
                            thumbnailImageUrl: ThumbnailImageUrl,
                            actions: aITemplateAction

                        )
                     );

                List<ISendMessage> MessageToSend = new List<ISendMessage>
            {
                ButtonsTemplateMessage,
            };

                await this.m_LineMessagingClient.PushMessageAsync(UserId, MessageToSend);

            }

            public async Task PostSerializedTemplate(string UserId, String AltText, String ThumbnailImageUrl, String Title, String Text, List<ITemplateAction> aITemplateAction)
            {
                try
                {
                    this.m_ToolUtilityClass.CreatePushLineMessage(UserId, "Line推播統計:Template", "");
                    ISendMessage ButtonsTemplateMessage = new TemplateMessage
                    (
                        AltText,
                        new ButtonsTemplate
                        (
                            text: Text,
                            title: Title,
                            thumbnailImageUrl: ThumbnailImageUrl,
                            actions: aITemplateAction

                        )
                     );

                    List<ISendMessage> MessageToSend = new List<ISendMessage>
                    {
                        ButtonsTemplateMessage,
                    };

                    await this.m_LineMessagingClient.PushMessageAsync(UserId, MessageToSend);

                }
                catch (System.Exception e)
                {
                    String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                    throw e;
                }
            }

            public async Task PostSerializedFlex(string UserId, FlexMessage aFlexMessage)
            {
            this.m_ToolUtilityClass.CreatePushLineMessage(UserId, "Line推播統計:Flex", "");
                await this.m_LineMessagingClient.PushMessageAsync(UserId, new List<ISendMessage> { aFlexMessage });
            }
            public async Task PostSerializedConfirm(string UserId, String AltText, String Text, List<ITemplateAction> aITemplateAction)
            {
                this.m_ToolUtilityClass.CreatePushLineMessage(UserId, "Line推播統計:Confirm", "");
                ISendMessage ConfirmTemplateMessage = new TemplateMessage
                    (
                        AltText,
                        new ConfirmTemplate(Text, actions: aITemplateAction)
                    );

                List<ISendMessage> MessageToSend = new List<ISendMessage>
                {
                    ConfirmTemplateMessage,
                };

                await this.m_LineMessagingClient.PushMessageAsync(UserId, MessageToSend);
            }
            public async Task PostSerializedImageMap(string UserId, string AltText, string ImageUrl, int BaseWidth, int Basehight, List<IImagemapAction> aImagemapAction)
            {
                this.m_ToolUtilityClass.CreatePushLineMessage(UserId, "Line推播統計:ImageMap", "");
                ISendMessage ImageMapTemplateMessage = new ImagemapMessage
                        (
                            ImageUrl, AltText,
                            new ImagemapSize(BaseWidth, Basehight),
                            aImagemapAction
                        );

                List<ISendMessage> MessageToSend = new List<ISendMessage>
                {
                    ImageMapTemplateMessage,
                };

                await this.m_LineMessagingClient.PushMessageAsync(UserId, MessageToSend);

            }

            public async Task<String> AddRichMenuMessage(string UserId)
            {
                RichMenu richMenu = new RichMenu()
                {
                    Size = ImagemapSize.RichMenuLong,
                    Selected = false,
                    Name = "nice richmenu",
                    ChatBarText = "touch me",
                    Areas = new List<ActionArea>()
                    {
                        new ActionArea()
                        {
                            Bounds = new ImagemapArea(0,0 ,ImagemapSize.RichMenuLong.Width,ImagemapSize.RichMenuLong.Height),
                            Action = new PostbackTemplateAction("ButtonA", "Menu A", "Menu A")
                        }
                    }
                };

                String richMenuId = await this.m_LineMessagingClient.CreateRichMenuAsync(richMenu);
                //var image = new MemoryStream(File.ReadAllBytes(HttpContext.Current.Server.MapPath(@"~\Images\richmenu.PNG")));
                //var image = new MemoryStream(File.ReadAllBytes(@"D:\\LINE 佈署\\Logo\\音訊科技\\SpeechMessage.png"));

                String path = @"D:\暫存區\richmenu.PNG";

                byte[] readText = File.ReadAllBytes(path);
                var image = new MemoryStream(readText);


                //var image = new MemoryStream(byDataValue);

                // Upload Image
                await this.m_LineMessagingClient.UploadRichMenuPngImageAsync(image, richMenuId);
                // Link to user
                await this.m_LineMessagingClient.LinkRichMenuToUserAsync(UserId, richMenuId);

                ISendMessage replyMessage = new TextMessage("Rich menu added");
                List<ISendMessage> MessageToSend = new List<ISendMessage>
            {
                replyMessage,
                new StickerMessage("1", "5")
            };

                await this.m_LineMessagingClient.PushMessageAsync(UserId, MessageToSend);

                return "成功";

            }
            public async Task<String> DeleteRichMenuMessage(string UserId)
            {
                // Get Rich Menu for the user
                var richMenuId = await this.m_LineMessagingClient.GetRichMenuIdOfUserAsync(UserId);
                await m_LineMessagingClient.UnLinkRichMenuFromUserAsync(UserId);
                await m_LineMessagingClient.DeleteRichMenuAsync(richMenuId);

                return "成功";

            }

            #endregion

            #endregion

            #region 設定通知格式

            public void SetupActionList(Entity aLetterEntity, ref TemplateMessageClass aTemplateMessageClass)
            {
                try
                {
                    String ActionLabel_1 = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aLetterEntity, "new_template_label_1");
                    if (ActionLabel_1 != "")
                    {
                        ActionClass aActionClass = new ActionClass()
                        {
                            type = ConvertActionType(aLetterEntity, "new_action_category_1"),
                            label = ActionLabel_1,
                            text = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aLetterEntity, "new_template_text_1"),
                            data = "動作=" + ActionLabel_1 + "& EntityId=" + aLetterEntity.Id,
                            uri = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aLetterEntity, "new_template_uri_1"),

                            //type = "postback",
                            //label = "購買",
                            //data = "action=購買&itemid=001",
                            //uri = "http://www.speechmessage.com.tw",
                        };
                        aTemplateMessageClass.template.actions.Add(aActionClass);
                    }
                }
                catch (System.Exception e)
                {
                    String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                    throw e;
                }
            }

            public String ConvertActionType(Entity aLetterEntity, String FieldName)
            {
                try
                {
                    int ActionType = this.m_ToolUtilityClass.GetOptionSetAttribute(ref aLetterEntity, FieldName);

                    switch (ActionType)
                    {
                        case 100000000:
                            {
                                return "postback";
                            }
                        case 100000001:
                            {
                                return "message";
                            }
                        case 100000002:
                            {
                                return "uri";
                            }
                        default:
                            {
                                return "";
                            }
                    }
                }
                catch (System.Exception e)
                {
                    String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                    throw e;
                }
            }
            #endregion

            #region 處理寄送者

            public Entity GetLineSender(Entity aLetterEntity)
            {
                try
                {
                    EntityCollection aFromEntityCollection = aLetterEntity.GetAttributeValue<EntityCollection>("from");

                    for (int i = 0; i < aFromEntityCollection.Entities.Count; i++)
                    {
                        #region 取得 LINE 訊息寄送者
                        EntityReference aContactEntityReference = (EntityReference)aFromEntityCollection.Entities[i]["partyid"];

                        Guid aContactId = aContactEntityReference.Id;

                        Entity aRetrievedContact = this.m_ToolUtilityClass.RetrieveEntity("contact", aContactId);

                        return aRetrievedContact;

                        #endregion
                    }

                    return null;
                }
                catch (System.Exception e)
                {
                    String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                    throw e;
                }
            }

            public void GetLineIdAndContactFullNameOfSender(Entity aLetterEntity, ref String DisplayedLineId, ref String LineId, ref String ReplyToken, ref String ContactFullName)
            {
                try
                {
                    EntityCollection aFromEntityCollection = aLetterEntity.GetAttributeValue<EntityCollection>("from");

                    for (int i = 0; i < aFromEntityCollection.Entities.Count; i++)
                    {
                        #region 取得 LINE 訊息收件者的全名及其LINE ID
                        LineId = "";
                        ContactFullName = GetContactPartyFullName(aFromEntityCollection.Entities[i], ref LineId);
                        #endregion
                    }

                    DisplayedLineId = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aLetterEntity, "new_displayed_lineid");

                    ReplyToken = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aLetterEntity, "new_linereplytoken");

                    return;
                }
                catch (System.Exception e)
                {
                    String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                    throw e;
                }
            }

            public string GetContactPartyFullName(Entity aContactParty, ref String LineId)
            {
                try
                {
                    EntityReference aContactEntityReference = (EntityReference)aContactParty["partyid"];

                    Guid aContactId = aContactEntityReference.Id;

                    String aContactName = aContactEntityReference.Name;

                    Entity aRetrievedContact = this.m_ToolUtilityClass.RetrieveEntity("contact", aContactId);

                    //if (aContactName.StartsWith("Line新加入者"))
                    //if (aContactName.EndsWith("(Line)"))
                    //{
                    //    aContactName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aRetrievedContact, "new_line_displayname");
                    //}

                    LineId = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aRetrievedContact, "new_lineid");

                    return aContactName;
                }
                catch (System.Exception e)
                {
                    String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                    throw e;
                }
            }

            #endregion


        }

        #region POST 區塊

        public class PostTextClass
        {
            public string to { get; set; }

            public List<TextMessageClass> messages { get; set; }
        }

        public class TextMessageClass
        {
            public string type { get; set; }
            public string text { get; set; }
        }







        public class PostTemplateClass
        {
            public string to { get; set; }

            public List<TemplateMessageClass> messages { get; set; }
        }
        public class TemplateMessageClass
        {
            public string type { get; set; }
            public string altText { get; set; }
            public TemplateClass template { get; set; }
        }
        public class TemplateClass
        {
            public string type { get; set; }
            public string thumbnailImageUrl { get; set; }
            public string title { get; set; }
            public string text { get; set; }
            public List<ActionClass> actions { get; set; }
        }




        public class PostConfirmClass
        {
            public string to { get; set; }

            public List<ConfirmMessageClass> messages { get; set; }
        }
        public class ConfirmMessageClass
        {
            public string type { get; set; }
            public string altText { get; set; }
            public ConfirmClass template { get; set; }
        }
        public class ConfirmClass
        {
            public string type { get; set; }
            public string text { get; set; }
            public List<ActionClass> actions { get; set; }
        }




        public class PostCarouselClass
        {
            public string to { get; set; }

            public List<CarouselMessageClass> messages { get; set; }
        }
        public class CarouselMessageClass
        {
            public string type { get; set; }
            public string altText { get; set; }
            public CarouselClass template { get; set; }
        }

        public class CarouselClass
        {
            public string type { get; set; }
            public List<CarouselColumeClass> columns { get; set; }
        }

        public class CarouselColumeClass
        {
            public string thumbnailImageUrl { get; set; }
            public string title { get; set; }
            public string text { get; set; }
            public List<ActionClass> actions { get; set; }
        }



        public class PostImageMapClass
        {
            public string to { get; set; }

            public List<ImageMapMessageClass> messages { get; set; }
        }
        public class ImageMapMessageClass
        {
            public string type { get; set; }
            public string baseUrl { get; set; }
            public string altText { get; set; }

            public BaseSizeClass baseSize { get; set; }

            public List<ActionClass> actions { get; set; }
        }

        public class BaseSizeClass
        {
            public int height { get; set; }
            public int width { get; set; }
        }






        public class ActionClass
        {
            public string type { get; set; }
            public string label { get; set; }
            public string data { get; set; }
            public string text { get; set; }
            public string uri { get; set; }
            public string linkUri { get; set; }


            public AreaClass area { get; set; }

        }

        public class AreaClass
        {
            public int x { get; set; }
            public int y { get; set; }
            public int width { get; set; }
            public int height { get; set; }
        }



        #endregion

        #region 寄發LINE所需的 Class

        public class MessageContent
        {
            public int ContentLength { get; set; }
            public string ContentType { get; set; }
            public List<byte> RawBytes { get; set; }
        }


        public class UserProfile
        {
            //"displayName":"LINE taro",
            //"userId":"Uxxxxxxxxxxxxxx...",
            //"pictureUrl":"http://obs.line-apps.com/...",
            //"statusMessage":"Hello, LINE!"
            public string DisplayName { get; set; }
            public string UserId { get; set; }
            public string PictureUrl { get; set; }
            public string StatusMessage { get; set; }
        }
        #endregion
}
