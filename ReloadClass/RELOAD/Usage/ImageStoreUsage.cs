#undef MAKE_IMAGE_STORE_APPATTACH

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using TSystems.RELOAD;
using TSystems.RELOAD.Storage;
using TSystems.RELOAD.Transport;
using TSystems.RELOAD.Usage;
using TSystems.RELOAD.Utils;

public struct ImageStoreData
{
	/// <summary>
	/// Node ID where image was offered.
	/// </summary>
	public NodeId NodeId { get; private set; }

	public string Name { get; private set; }
	public int Width { get; private set; }
	public int Height { get; private set; }
	public byte[] Data { get; private set; }

	public ImageStoreData(NodeId nodeId, string name, int width, int height, byte[] data)
		: this()
	{
		this.NodeId = nodeId;
		this.Name = name;
		this.Width = width;
		this.Height = height;
		this.Data = data;
	}
}

public sealed class ImageStoreUsage : IUsage
{
	/// <summary>
	/// Usage manager this usage belongs to.
	/// </summary>
	private readonly UsageManager UsageManager;

	// Certain static IUsage members
	public Usage_Code_Point CodePoint { get { return Usage_Code_Point.IMAGE_STORE; } }
	public string Name { get { return "image-store"; } }
	public uint KindId { get { return 0xC0FFEE; } }
	public ReloadGlobals.DataModel DataModel() { return ReloadGlobals.DataModel.DICTIONARY; }

	// Certain dynamic IUsage members
	public string ResourceName { get; set; }
	public uint Length { get; private set; }

	/// <summary>
	/// Stored image storedData.
	/// </summary>
	public ImageStoreData Data { get; private set; }

	/// <summary>
	/// See also the usage registration procedure at <seealso cref="Machine.InitUsageManager"/>.
	/// </summary>
	public ImageStoreUsage(UsageManager UsageManager)
	{
		this.UsageManager = UsageManager;
	}

	public IUsage Create(int? type, params object[] arguments)
	{
		if (arguments == null || arguments.Length != 5)
			throw new ArgumentException(
				"Expected arguments are [0] ResourceName, [1] Image Name, [2] Width, [3] Height and [4] Data!");

		// Initialize the usage with given arguments
		ImageStoreUsage result = new ImageStoreUsage(UsageManager)
		{
			ResourceName = (string)arguments[0],
			Data = new ImageStoreData(
				UsageManager.localNode.Id,
				(string)arguments[1],
				(int)arguments[2],
				(int)arguments[3],
				(byte[])arguments[4])
		};

		// Compute the total usage length
		result.Length = 0
			+ (uint)(result.ResourceName.Length + 2)
			+ (uint)(result.Data.NodeId.Digits)
			+ (uint)(result.Data.Name.Length + 2)
			+ (uint)(2)
			+ (uint)(2)
			+ (uint)(result.Data.Data.Length + 2);

		return result;
	}

	public uint dump(BinaryWriter writer)
	{
		const ulong MAX_VALUE = 0xFFFFFFFF;
		var ASCII = Encoding.ASCII;

		// Serialize the ResourceName
		ReloadGlobals.WriteOpaqueValue(writer, ASCII.GetBytes(ResourceName), MAX_VALUE);

		// Serialize the ImageStoreData
		writer.Write(Data.NodeId.Data);
		ReloadGlobals.WriteOpaqueValue(writer, ASCII.GetBytes(Data.Name), MAX_VALUE);
		writer.Write(IPAddress.HostToNetworkOrder((short)Data.Width));
		writer.Write(IPAddress.HostToNetworkOrder((short)Data.Height));
		ReloadGlobals.WriteOpaqueValue(writer, Data.Data, MAX_VALUE);

		return Length;
	}

	public IUsage FromReader(ReloadMessage rm, BinaryReader reader, long reload_msg_size)
	{
		ImageStoreUsage result = new ImageStoreUsage(UsageManager);
		var ASCII = Encoding.ASCII;
		var bytesCount = 0;

		try
		{
			// Deserialize the ResourceName
			{
				bytesCount = IPAddress.NetworkToHostOrder(reader.ReadInt32());
				result.ResourceName = ASCII.GetString(reader.ReadBytes(bytesCount), 0, bytesCount);
			}

			// Deserialize the ImageStoreData
			{
				var nodeId = new NodeId(reader.ReadBytes(ReloadGlobals.NODE_ID_DIGITS));

				bytesCount = IPAddress.NetworkToHostOrder(reader.ReadInt32());
				var name = ASCII.GetString(reader.ReadBytes(bytesCount), 0, bytesCount);

				var width = IPAddress.NetworkToHostOrder(reader.ReadInt16());

				var height = IPAddress.NetworkToHostOrder(reader.ReadInt16());

				bytesCount = IPAddress.NetworkToHostOrder(reader.ReadInt32());
				var data = reader.ReadBytes(bytesCount);

				result.Data = new ImageStoreData(nodeId, name, width, height, data);
			}

			// Compute the total usage length
			result.Length = 0
				+ (uint)(result.ResourceName.Length + 2)
				+ (uint)(result.Data.NodeId.Digits)
				+ (uint)(result.Data.Name.Length + 2)
				+ (uint)(2)
				+ (uint)(2)
				+ (uint)(result.Data.Data.Length + 2);
		}
		catch (Exception exception)
		{
			UsageManager.m_ReloadConfig.Logger(ReloadGlobals.TRACEFLAGS.T_ERROR,
				 String.Format("ImageStoreUsage.FromBytes(): {0}", exception.Message));
		}

		return result;
	}

	public void AppProcedure(MessageTransport transport, List<FetchKindResponse> fetchKindResponses)
	{
#if MAKE_IMAGE_STORE_APPATTACH // Make AppAttach

		FetchKindResponse fetchResponse = null;

		// Pick up a fetch response with the own kind id
		var _fetchKindResponses = new List<FetchKindResponse>(fetchKindResponses); // copy of fetchKindResponses
		foreach (FetchKindResponse fetchKindResponse in _fetchKindResponses)
		{
			if (fetchResponse.kind == this.KindId)
			{
				fetchResponse = fetchKindResponse;
				break;
			}
		}

		// Release all fetch responses
		fetchKindResponses.Clear();

		if (fetchResponse == null)
			return;

		// Make an AppAttach request for every attached usage
		var storedDatas = fetchResponse.values;
		foreach (StoredData storedData in storedDatas)
		{
			ImageStoreUsage usage = (ImageStoreUsage)storedData.Value.GetUsageValue;

			Arbiter.Activate(UsageManager.m_DispatcherQueue,
				new IterativeTask<Destination>(
					new Destination(usage.Data.NodeId),
				transport.AppAttachProcedure));
		}

#else // Don't make AppAttach

		fetchKindResponses.Clear();

#endif
	}

	public StoredDataValue Encapsulate(bool exists)
	{
		return new StoredDataValue(
			UsageManager.localNode.Id.ToString(), this, exists);
	}

	public string Report()
	{
		return String.Format("ImageStoreUsage: [Total Usage Length] {0} [ResourceName] {1} [NodeId] {2} [Name] {3} [Width] {4} [Height] {5} [Data Length] {6}",
			Length, ResourceName, Data.NodeId, Data.Name, Data.Width, Data.Height, Data.Data.Length);
	}
}