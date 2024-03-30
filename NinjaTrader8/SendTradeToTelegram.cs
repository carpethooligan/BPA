#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.Gui.Tools;
using System.Net;
using System.Diagnostics;
using NinjaTrader.Core;
using NinjaTrader.CQG.ProtoBuf;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TextBox;
using System.Windows.Markup;
using System.Web.Script.Serialization;
#endregion

//This namespace holds Add ons in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.AddOns
{
	
	
	public class SendTradeToTelegram : NinjaTrader.NinjaScript.AddOnBase
	{

        /* ==== SETTINGS ==== */

        /* AccountName
		 * Name of the account from which to send trades, for example: Sim101
		 * This helps to know for which trades to send the entry and exit information.
		 * No actual account details will be sent by the script. 		  
		 */
		private string AccountName = "Sim101";


        /* TraderToken
		 * The secret token obtained from Butler bot on Telegram.
		 * This helps it know which trader in group is sending the trades.
		 * Send a private message to bot and say /token
		 */
        private string TraderToken = "XXX-XXX-XXX";

        /* BotURL
		 * The place where Butler bot recieves the trades before posting to Telegram.
		 */
        private string BotURL = "https://api.bpachat.com/v1/";


        /* Symbols
		 * Only these symbols will be sent by the script . 
		 * Ignore the expiration month for Futures, use only the main symbol name.
		 */
        private List<string> SymbolsToSend = new List<string>() { "ES", "MES", "MGC", "M6E", "SPX", "EURUSD" };

		/* Delay
		 * How many seconds for bot to wait before announcing trade in chat. Max 60 seconds.
		 * Helps to avoid trader copiers.
		 */
		int SecondsDelay = 10;


        /* === DON'T CHANGE ANYTHING BELOW === */
        private string VER = "1.4";
		private string COPYRIGHT = "Copyright 2024, BPA Chat";
        private NinjaTrader.Cbi.Account account;
		private NinjaTrader.Cbi.Order myOrder;
		DateTime lastTradeExitTime = DateTime.MinValue;

		protected override void OnStateChange()
		{
			try
			{
				if (State == State.SetDefaults)
				{
					Description									= @"Send entry and exits for specific instruments to BPA Chat on Telegram";
					Name										= "SendTradeToTelegram";
				}
				else if (State == State.Configure) 
				{
					account = Cbi.Account.All.FirstOrDefault(a => a.Name == AccountName);
					account.ExecutionUpdate += OnExecutionUpdate;
                }  
				else if (State == State.Active)
				{
					TradeCollection allTrades = GetAllTrades();
					if (allTrades.Count > 0) lastTradeExitTime = allTrades.Last().Exit.Time;
                }
                else if(State == State.Terminated)
				{
                    account.ExecutionUpdate -= OnExecutionUpdate;
                }
			}
			catch(Exception err)
			{
				Print(err);
			}			
			
		}

        public TradeCollection GetAllTrades()
		{
            List<Execution> executions = new List<Execution>();

            foreach (Execution cae in account.Executions.OrderBy(c => c.Time))
            {
                executions.Add(cae);
            }

			TradeCollection allTrades = SystemPerformance.Calculate(executions).AllTrades;
			
			return allTrades;
        }


        private void OnExecutionUpdate(object sender, ExecutionEventArgs e)
        {
			try
			{
				string symbol = e.Execution.Instrument.MasterInstrument.Name;

				if (!SymbolsToSend.Contains(symbol)) return;
				if (e.Execution.Order == null) return;
				if (e.Execution.Order.OrderState == OrderState.PartFilled) return;
				
                int direction = e.Execution.Order.IsLong == true ? 1 : -1;
                string market = Enum.GetName(e.Execution.Instrument.MasterInstrument.InstrumentType.GetType(), e.Execution.Instrument.MasterInstrument.InstrumentType);

				// if an entry is detected
				if (e.Execution.IsEntry)
				{
					SendEntry(TraderToken, System.Guid.NewGuid().ToString(), symbol, market, e.Execution.Time.ToString("yyyy-MM-dd HH:mm:ss zzz"), direction, e.Price, SecondsDelay);
					return;
				}

                // if not an exit then abort
                if (!e.Execution.IsExit) return;							


				TradeCollection lastTrades = GetAllTrades();

				foreach (Cbi.Trade lastTrade in lastTrades.Where(t => t.Exit.Time > lastTradeExitTime).ToList())
				{
					double pl = 0;

					switch (e.Execution.Instrument.MasterInstrument.InstrumentType)
					{
						case InstrumentType.Future:
							pl = e.MarketPosition == MarketPosition.Long ?
                                lastTrade.Entry.Price - lastTrade.Exit.Price :
								lastTrade.Exit.Price - lastTrade.Entry.Price; break; // as if trading 1 contract
						case InstrumentType.Forex:
							pl = (lastTrade.ProfitPips / lastTrade.Quantity) * account.ForexLotSize; break; // as if trading 1 lot
                    };
					
                    SendExit(TraderToken, System.Guid.NewGuid().ToString(), symbol, market, lastTrade.Entry.Time.ToString("yyyy-MM-dd HH:mm:ss zzz"), lastTrade.Exit.Time.ToString("yyyy-MM-dd HH:mm:ss zzz"), lastTrade.Entry.Price, lastTrade.Exit.Price, direction*-1, Math.Round(pl, 4), SecondsDelay, e.Execution.IsLastExit);

                }

				lastTradeExitTime = lastTrades.Last().Exit.Time; 
			}
            catch (Exception err)
            {
                Print(err);
            }
        }

        private void SendEntry(string traderToken, string trade_id, string symbol, string market, string entry_time, int direction, double entry_price, int secondsDelay)
        {
            Task.Run(() =>
            {
                try
                {
                    using (var wb = new WebClient())
                    {
                        object data = new { token = traderToken, trade_id = trade_id, symbol = symbol, market = market, entry_time = entry_time, entry_price = entry_price, direction = direction, delay = secondsDelay, version = VER, copyright = COPYRIGHT};

                        var serializer = new JavaScriptSerializer();
                        var datastring = serializer.Serialize(data);

                        wb.Headers.Add(HttpRequestHeader.ContentType, "application/json");

                        var response = wb.UploadString(new Uri(BotURL + "trade/entry"), "POST", datastring);

                        if (response != "OK")
                            Print("Addon SendTradeToTelegram::SendEntry() response: " + response);

                    }
                }
                catch (Exception ex)
                {
                    Print(ex.ToString());
                }
            }
);

        }

        private void SendExit(string token, string trade_id, string symbol, string market, string entry_time, string exit_time, double entry_price, double exit_price, int direction, double pl, int delay,  bool flat)
		{
            Task.Run(() =>
            {
				try
				{
					using (var wb = new WebClient())
					{

						int isFlat = flat ? 1 : 0;

						object data = new { token = token, trade_id = trade_id, symbol = symbol, market = market, entry_time = entry_time, exit_time = exit_time, entry_price = entry_price, exit_price = exit_price, direction = direction, pl = pl, delay = delay, flat = isFlat, version = VER, copyright = COPYRIGHT};

						var serializer = new JavaScriptSerializer();
						var datastring = serializer.Serialize(data);

						wb.Headers.Add(HttpRequestHeader.ContentType, "application/json");

						var response = wb.UploadString(new Uri(BotURL + "trade/exit"), "POST", datastring);

						if (response != "OK")
							Print("Addon SendTradeToTelegram::SendExit() response: " + response);

					}

				} catch (Exception ex)
				{
					Print(ex.ToString());
				}
            }
            );
        }




    }
}
