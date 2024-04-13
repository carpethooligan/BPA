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
	
	
	public class SendTradeToTelegramTest : NinjaTrader.NinjaScript.AddOnBase
	{

        /* ==== SETTINGS ==== */

        /* AccountName
		 * Name of the account from which to send trades, for example: Sim101
		 * This helps to know for which trades to send the entry and exit information.
		 * No actual account details will be sent by the script. 		  
		 */
        private string AccountName = "Sim2";


        /* TraderToken
		 * The secret token obtained from Butler bot on Telegram.
		 * This helps it know which trader in group is sending the trades.
		 * Send a private message to bot and say /token
		 */
        private string TraderToken = "";

        /* BotURL
		 * The place where Butler bot recieves the trades before posting to Telegram.
		 */
		//private string BotURL = "https://api.bpachat.com/v1/";
        private string BotURL = "https://sandbox.bpachat.com/v1/";
        //private string BotURL = "http://localhost/v1/";



        /* Symbols
		 * Only these symbols will be sent by the script . 
		 * Ignore the expiration month for Futures, use only the main symbol name.
		 */
        private List<string> SymbolsToSend = new List<string>() { "ES", "MES", "MGC", "M6E", "SPX", "EURUSD" };

		/* Delay
		 * How many seconds for bot to wait before announcing trade in chat. Max 60 seconds.
		 * Helps to avoid trader copiers.
		 */
		int SecondsDelay = 1;


        /* === DON'T CHANGE ANYTHING BELOW === */
        private string VER = "1.6";
		private string COPYRIGHT = "Copyright 2024, BPA Chat";
        private NinjaTrader.Cbi.Account account;	
		private Dictionary<string,SmartPositionTracker> masterTracker = new Dictionary<string,SmartPositionTracker>();
		
		public class SmartPositionTracker
		{
			public DateTime entryTime;
			public double entryPrice;
			public double positionAvgPrice;
			public MarketPosition currentPosition = MarketPosition.Flat;
			public NinjaTrader.Cbi.Order lastFilledOrder = null;
		}
		
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
					account.PositionUpdate += OnPositionUpdate;
					account.OrderUpdate += OnOrderUpdate;
                }  
				else if (State == State.Active)
				{
					foreach (Position position in account.Positions)
	           		{
						masterTracker.Add(position.Instrument.MasterInstrument.Name, new SmartPositionTracker());
						masterTracker[position.Instrument.MasterInstrument.Name].currentPosition = position.MarketPosition;
	           		}									
                }
                else if(State == State.Terminated)
				{
					account.OrderUpdate -= OnOrderUpdate;
					account.PositionUpdate -= OnPositionUpdate;
                }
			}
			catch(Exception err)
			{
				Print(err);
			}			
			
		}
		
		private double calcPL(InstrumentType instrType, int pos, double entryPrice, double exitPrice)
		{
			double pl = 0;

			switch (instrType)
			{
				case InstrumentType.Cfd:
				case InstrumentType.Stock:
				case InstrumentType.Future:
					// don't include size in calculation so as if trading 1 contract
					pl = pos == 1 ?
                        entryPrice - exitPrice :
						exitPrice - entryPrice; break; 
				case InstrumentType.Forex:
					// uhh... will figure out later. 
					break;
            };			
			
			return pl;
		}
		
		private void OnPositionUpdate(object sender, PositionEventArgs e) 
		{ 
			
			try 
			{
				
				string instrument = e.Position.Instrument.MasterInstrument.Name; 
				SmartPositionTracker SPT = masterTracker[instrument];
				
				if(SPT == null || SPT.lastFilledOrder == null) return;
				
				if (!SymbolsToSend.Contains(instrument)) return;			
                int direction = SPT.lastFilledOrder.IsLong ? 1 : -1;
                string market = Enum.GetName(e.Position.Instrument.MasterInstrument.InstrumentType.GetType(), e.Position.Instrument.MasterInstrument.InstrumentType);								
				
				// if this new position is flat then this is an exit
				if(e.MarketPosition == MarketPosition.Flat)
				{					
					double pl = calcPL(e.Position.Instrument.MasterInstrument.InstrumentType, direction, SPT.positionAvgPrice, SPT.lastFilledOrder.AverageFillPrice);
					SendExit(TraderToken, System.Guid.NewGuid().ToString(), instrument, market, SPT.entryTime.ToString("yyyy-MM-dd HH:mm:ss zzz"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"), SPT.entryPrice, SPT.lastFilledOrder.AverageFillPrice, direction*-1, Math.Round(pl, 4), SecondsDelay, true);
					Print("Exit for PL:" + pl + " PosAvgPrice:" + e.Position.AveragePrice + " LastOrderPrice:" + SPT.lastFilledOrder.AverageFillPrice);
				}	
				// if saved position was flat then this is an entry
				else if(SPT.currentPosition == MarketPosition.Flat)
				{
					SPT.entryTime = DateTime.Now;
					SPT.entryPrice = SPT.lastFilledOrder.AverageFillPrice;					
					SPT.positionAvgPrice = e.Position.AveragePrice;
					
					SendEntry(TraderToken, System.Guid.NewGuid().ToString(), instrument, market, SPT.entryTime.ToString("yyyy-MM-dd HH:mm:ss zzz"), direction, SPT.entryPrice, SecondsDelay);
					Print("Entry " + e.MarketPosition + " at " + SPT.entryTime + " @ " + SPT.entryPrice);
				}
				else
				{
					// when clicking Reverse button on a large position the overall state may jump from Long directly to Short, depending on how Ninja fills partials for the big order, without going through Flat first
					if((SPT.currentPosition == MarketPosition.Long && e.MarketPosition == MarketPosition.Short) || (SPT.currentPosition == MarketPosition.Short && e.MarketPosition == MarketPosition.Long))
					{
							double pl = calcPL(e.Position.Instrument.MasterInstrument.InstrumentType, direction, SPT.positionAvgPrice, SPT.lastFilledOrder.AverageFillPrice);
							SendExit(TraderToken, System.Guid.NewGuid().ToString(), instrument, market, SPT.entryTime.ToString("yyyy-MM-dd HH:mm:ss zzz"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"), SPT.entryPrice, SPT.lastFilledOrder.AverageFillPrice, direction*-1, Math.Round(pl, 4), SecondsDelay, true);
							SPT.positionAvgPrice = e.Position.AveragePrice;							
							Print("Exiting to Reverse");
														
							System.Threading.Thread.Sleep(1000);
								
							SendEntry(TraderToken, System.Guid.NewGuid().ToString(), instrument, market, SPT.entryTime.ToString("yyyy-MM-dd HH:mm:ss zzz"), direction, SPT.lastFilledOrder.AverageFillPrice, SecondsDelay);							
							Print("Entering new after reversal");							
							

					}														
					// if saved position was long and current is long and filled order was long then this is an addon to a long
					else if(SPT.currentPosition == MarketPosition.Long && e.MarketPosition == MarketPosition.Long && SPT.lastFilledOrder.IsLong)
					{
						if(!isFlood()) 
						{
							SPT.positionAvgPrice = e.Position.AveragePrice;							
							SendEntry(TraderToken, System.Guid.NewGuid().ToString(), instrument, market, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"), direction, SPT.lastFilledOrder.AverageFillPrice, SecondsDelay);
							Print("Addon to long");
						}
					}
					
					else if(SPT.currentPosition == MarketPosition.Short && e.MarketPosition == MarketPosition.Short && SPT.lastFilledOrder.IsShort)
					{
						if(!isFlood()) 
						{
							SPT.positionAvgPrice = e.Position.AveragePrice;							
							SendEntry(TraderToken, System.Guid.NewGuid().ToString(), instrument, market, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"), direction, SPT.lastFilledOrder.AverageFillPrice, SecondsDelay);						
							Print("Addon to short");
						}
					}
					
					// if saved position was long and current is long and filled order was a sell then this is a scaleout from a long
					else if(SPT.currentPosition == MarketPosition.Long && e.MarketPosition == MarketPosition.Long && SPT.lastFilledOrder.IsShort)
					{
						double pl = calcPL(e.Position.Instrument.MasterInstrument.InstrumentType, direction, SPT.positionAvgPrice, SPT.lastFilledOrder.AverageFillPrice);
						SendExit(TraderToken, System.Guid.NewGuid().ToString(), instrument, market, SPT.entryTime.ToString("yyyy-MM-dd HH:mm:ss zzz"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"), SPT.entryPrice, SPT.lastFilledOrder.AverageFillPrice, direction*-1, Math.Round(pl, 4), SecondsDelay, false);
						SPT.positionAvgPrice = e.Position.AveragePrice;							
						Print("Scaled out from a long");
					}
					else if(SPT.currentPosition == MarketPosition.Short && e.MarketPosition == MarketPosition.Short && SPT.lastFilledOrder.IsLong)
					{
						double pl = calcPL(e.Position.Instrument.MasterInstrument.InstrumentType, direction, SPT.positionAvgPrice, SPT.lastFilledOrder.AverageFillPrice);
						SendExit(TraderToken, System.Guid.NewGuid().ToString(), instrument, market, SPT.entryTime.ToString("yyyy-MM-dd HH:mm:ss zzz"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"), SPT.entryPrice, SPT.lastFilledOrder.AverageFillPrice, direction*-1, Math.Round(pl, 4), SecondsDelay, false);
						SPT.positionAvgPrice = e.Position.AveragePrice;													
						Print("Scaled out from a short");
					}
					
					
				}
												
				masterTracker[instrument].currentPosition = e.MarketPosition;
				masterTracker[instrument].lastFilledOrder = null;
			} 
			catch (Exception err)
			{
				Print(err.ToString());
			}
			
		}
		
		private bool isFlood()
		{
			// avoid order floods like when Reverse is clicked and Ninja keeps filling partially
			// a flood checks the last 2 orders to see how soon one follows the other
			List<DateTime> recentOrderTimes = account.Orders.Reverse().Take(2).Select(o => o.Time).ToList();
			if(recentOrderTimes.Count == 2)
			{
				if( (recentOrderTimes[0] - recentOrderTimes[1]).TotalSeconds <= 2) return true;
			}

			return false;
		}
		
		 private void OnOrderUpdate(object sender, OrderEventArgs e)
      	{		
			try
			{			
				if(e.OrderState == OrderState.Filled)
				{			
					string instrument = e.Order.Instrument.MasterInstrument.Name;				
					if (!SymbolsToSend.Contains(instrument)) return;			
					
					if(!masterTracker.ContainsKey(instrument))
						masterTracker.Add(instrument, new SmartPositionTracker());
										
					masterTracker[instrument].lastFilledOrder = e.Order;

				}
			}
			catch (Exception err)
			{
				Print(err.ToString());
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
                            Print("SendTradeToTelegram::SendEntry() response: " + response);

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
							Print("SendTradeToTelegram::SendExit() response: " + response);

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


