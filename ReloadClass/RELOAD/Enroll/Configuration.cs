﻿/* * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * *
* Copyright (C) 2012, Telekom Deutschland GmbH 
*
* This file is part of RELOAD.NET.
*
* RELOAD.NET is free software: you can redistribute it and/or modify
* it under the terms of the GNU Lesser General Public License as published by
* the Free Software Foundation, either version 3 of the License, or
* (at your option) any later version.
*
* RELOAD.NET is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
* GNU Lesser General Public License for more details.
*
* You should have received a copy of the GNU Lesser General Public License
* along with RELOAD.NET.  If not, see <http://www.gnu.org/licenses/>.
*
* see https://github.com/RELOAD-NET/RELOAD.NET
* 
* * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * */

using System.Xml.Serialization;

// 
// This source code was auto-generated by xsd, Version=2.0.50727.1432.
// 


/// <remarks/>
[System.CodeDom.Compiler.GeneratedCodeAttribute("xsd", "2.0.50727.1432")]
[System.SerializableAttribute()]
[System.Diagnostics.DebuggerStepThroughAttribute()]
[System.ComponentModel.DesignerCategoryAttribute("code")]
[System.Xml.Serialization.XmlTypeAttribute(TypeName = "overlay-element", Namespace = "urn:ietf:params:xml:ns:p2p:config-base")]
[System.Xml.Serialization.XmlRootAttribute("overlay", Namespace = "urn:ietf:params:xml:ns:p2p:config-base", IsNullable = false)]
public partial class overlayelement {

  private configuration configurationField;

  private signature signatureField;

  /// <remarks/>
  public configuration configuration {
    get {
      return this.configurationField;
    }
    set {
      this.configurationField = value;
    }
  }

  /// <remarks/>
  public signature signature {
    get {
      return this.signatureField;
    }
    set {
      this.signatureField = value;
    }
  }
}

/// <remarks/>
[System.CodeDom.Compiler.GeneratedCodeAttribute("xsd", "2.0.50727.1432")]
[System.SerializableAttribute()]
[System.Diagnostics.DebuggerStepThroughAttribute()]
[System.ComponentModel.DesignerCategoryAttribute("code")]
[System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true, Namespace = "urn:ietf:params:xml:ns:p2p:config-base")]
[System.Xml.Serialization.XmlRootAttribute(Namespace = "urn:ietf:params:xml:ns:p2p:config-base", IsNullable = false)]
public partial class configuration : parameter {

  private string instancenameField;

  private System.DateTime expirationField;

  private bool expirationFieldSpecified;

  private long sequenceField;

  private bool sequenceFieldSpecified;

  private System.Xml.XmlAttribute[] anyAttrField;

  /// <remarks/>
  [System.Xml.Serialization.XmlAttributeAttribute("instance-name")]
  public string instancename {
    get {
      return this.instancenameField;
    }
    set {
      this.instancenameField = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlAttributeAttribute()]
  public System.DateTime expiration {
    get {
      return this.expirationField;
    }
    set {
      this.expirationField = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlIgnoreAttribute()]
  public bool expirationSpecified {
    get {
      return this.expirationFieldSpecified;
    }
    set {
      this.expirationFieldSpecified = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlAttributeAttribute()]
  public long sequence {
    get {
      return this.sequenceField;
    }
    set {
      this.sequenceField = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlIgnoreAttribute()]
  public bool sequenceSpecified {
    get {
      return this.sequenceFieldSpecified;
    }
    set {
      this.sequenceFieldSpecified = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlAnyAttributeAttribute()]
  public System.Xml.XmlAttribute[] AnyAttr {
    get {
      return this.anyAttrField;
    }
    set {
      this.anyAttrField = value;
    }
  }
}

/// <remarks/>
[System.CodeDom.Compiler.GeneratedCodeAttribute("xsd", "2.0.50727.1432")]
[System.SerializableAttribute()]
[System.Diagnostics.DebuggerStepThroughAttribute()]
[System.ComponentModel.DesignerCategoryAttribute("code")]
[System.Xml.Serialization.XmlTypeAttribute(Namespace = "urn:ietf:params:xml:ns:p2p:config-base")]
public partial class parameter {

  private string topologypluginField;

  private uint maxmessagesizeField;

  private bool maxmessagesizeFieldSpecified;

  private int initialttlField;

  private bool initialttlFieldSpecified;

  private string rootcertField;

  private kindblock[] requiredkindsField;

  private string[] enrollmentserverField;

  private string kindsignerField;

  private string configurationsignerField;

  private string[] badnodeField;

  private bool noiceField;

  private bool noiceFieldSpecified;

  private string sharedsecretField;

  private string overlaylinkprotocolField;

  private bool clientspermittedField;

  private bool clientspermittedFieldSpecified;

  private byte turndensityField;

  private bool turndensityFieldSpecified;

  private int nodeidlengthField;

  private bool nodeidlengthFieldSpecified;

  private string mandatoryextensionField;

  private selfsignedpermitted selfsignedpermittedField;

  private bootstrapnode[] bootstrapnodeField;

  private multicastbootstrap[] multicastbootstrapField;

  private string reportingurlField;

  private int chordpingintervalField;

  private bool chordpingintervalFieldSpecified;

  private int chordupdateintervalField;

  private bool chordupdateintervalFieldSpecified;

  private bool chordreactiveField;

  private bool chordreactiveFieldSpecified;

  private landmarks landmarksField;

  /// <remarks/>
  [System.Xml.Serialization.XmlElementAttribute("topology-plugin")]
  public string topologyplugin {
    get {
      return this.topologypluginField;
    }
    set {
      this.topologypluginField = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlElementAttribute("max-message-size")]
  public uint maxmessagesize {
    get {
      return this.maxmessagesizeField;
    }
    set {
      this.maxmessagesizeField = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlIgnoreAttribute()]
  public bool maxmessagesizeSpecified {
    get {
      return this.maxmessagesizeFieldSpecified;
    }
    set {
      this.maxmessagesizeFieldSpecified = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlElementAttribute("initial-ttl")]
  public int initialttl {
    get {
      return this.initialttlField;
    }
    set {
      this.initialttlField = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlIgnoreAttribute()]
  public bool initialttlSpecified {
    get {
      return this.initialttlFieldSpecified;
    }
    set {
      this.initialttlFieldSpecified = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlElementAttribute("root-cert")]
  public string rootcert {
    get {
      return this.rootcertField;
    }
    set {
      this.rootcertField = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlArrayAttribute("required-kinds")]
  [System.Xml.Serialization.XmlArrayItemAttribute("kind-block", IsNullable = false)]
  public kindblock[] requiredkinds {
    get {
      return this.requiredkindsField;
    }
    set {
      this.requiredkindsField = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlElementAttribute("enrollment-server", DataType = "anyURI")]
  public string[] enrollmentserver {
    get {
      return this.enrollmentserverField;
    }
    set {
      this.enrollmentserverField = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlElementAttribute("kind-signer")]
  public string kindsigner {
    get {
      return this.kindsignerField;
    }
    set {
      this.kindsignerField = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlElementAttribute("configuration-signer")]
  public string configurationsigner {
    get {
      return this.configurationsignerField;
    }
    set {
      this.configurationsignerField = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlElementAttribute("bad-node")]
  public string[] badnode {
    get {
      return this.badnodeField;
    }
    set {
      this.badnodeField = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlElementAttribute("no-ice")]
  public bool noice {
    get {
      return this.noiceField;
    }
    set {
      this.noiceField = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlIgnoreAttribute()]
  public bool noiceSpecified {
    get {
      return this.noiceFieldSpecified;
    }
    set {
      this.noiceFieldSpecified = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlElementAttribute("shared-secret")]
  public string sharedsecret {
    get {
      return this.sharedsecretField;
    }
    set {
      this.sharedsecretField = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlElementAttribute("overlay-link-protocol")]
  public string overlaylinkprotocol {
    get {
      return this.overlaylinkprotocolField;
    }
    set {
      this.overlaylinkprotocolField = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlElementAttribute("clients-permitted")]
  public bool clientspermitted {
    get {
      return this.clientspermittedField;
    }
    set {
      this.clientspermittedField = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlIgnoreAttribute()]
  public bool clientspermittedSpecified {
    get {
      return this.clientspermittedFieldSpecified;
    }
    set {
      this.clientspermittedFieldSpecified = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlElementAttribute("turn-density")]
  public byte turndensity {
    get {
      return this.turndensityField;
    }
    set {
      this.turndensityField = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlIgnoreAttribute()]
  public bool turndensitySpecified {
    get {
      return this.turndensityFieldSpecified;
    }
    set {
      this.turndensityFieldSpecified = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlElementAttribute("node-id-length")]
  public int nodeidlength {
    get {
      return this.nodeidlengthField;
    }
    set {
      this.nodeidlengthField = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlIgnoreAttribute()]
  public bool nodeidlengthSpecified {
    get {
      return this.nodeidlengthFieldSpecified;
    }
    set {
      this.nodeidlengthFieldSpecified = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlElementAttribute("mandatory-extension")]
  public string mandatoryextension {
    get {
      return this.mandatoryextensionField;
    }
    set {
      this.mandatoryextensionField = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlElementAttribute("self-signed-permitted")]
  public selfsignedpermitted selfsignedpermitted {
    get {
      return this.selfsignedpermittedField;
    }
    set {
      this.selfsignedpermittedField = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlElementAttribute("bootstrap-node")]
  public bootstrapnode[] bootstrapnode {
    get {
      return this.bootstrapnodeField;
    }
    set {
      this.bootstrapnodeField = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlElementAttribute("multicast-bootstrap")]
  public multicastbootstrap[] multicastbootstrap {
    get {
      return this.multicastbootstrapField;
    }
    set {
      this.multicastbootstrapField = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlElementAttribute("reporting-url", DataType = "anyURI")]
  public string reportingurl {
    get {
      return this.reportingurlField;
    }
    set {
      this.reportingurlField = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlElementAttribute("chord-ping-interval", Namespace = "urn:ietf:params:xml:ns:p2p:config-chord")]
  public int chordpinginterval {
    get {
      return this.chordpingintervalField;
    }
    set {
      this.chordpingintervalField = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlIgnoreAttribute()]
  public bool chordpingintervalSpecified {
    get {
      return this.chordpingintervalFieldSpecified;
    }
    set {
      this.chordpingintervalFieldSpecified = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlElementAttribute("chord-update-interval", Namespace = "urn:ietf:params:xml:ns:p2p:config-chord")]
  public int chordupdateinterval {
    get {
      return this.chordupdateintervalField;
    }
    set {
      this.chordupdateintervalField = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlIgnoreAttribute()]
  public bool chordupdateintervalSpecified {
    get {
      return this.chordupdateintervalFieldSpecified;
    }
    set {
      this.chordupdateintervalFieldSpecified = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlElementAttribute("chord-reactive", Namespace = "urn:ietf:params:xml:ns:p2p:config-chord")]
  public bool chordreactive {
    get {
      return this.chordreactiveField;
    }
    set {
      this.chordreactiveField = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlIgnoreAttribute()]
  public bool chordreactiveSpecified {
    get {
      return this.chordreactiveFieldSpecified;
    }
    set {
      this.chordreactiveFieldSpecified = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlElementAttribute(Namespace = "urn:ietf:params:xml:ns:p2p:config-base:disco")]
  public landmarks landmarks {
    get {
      return this.landmarksField;
    }
    set {
      this.landmarksField = value;
    }
  }
}

/// <remarks/>
[System.CodeDom.Compiler.GeneratedCodeAttribute("xsd", "2.0.50727.1432")]
[System.SerializableAttribute()]
[System.Diagnostics.DebuggerStepThroughAttribute()]
[System.ComponentModel.DesignerCategoryAttribute("code")]
[System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true, Namespace = "urn:ietf:params:xml:ns:p2p:config-base")]
[System.Xml.Serialization.XmlRootAttribute("kind-block", Namespace = "urn:ietf:params:xml:ns:p2p:config-base", IsNullable = false)]
public partial class kindblock {

  private kind kindField;

  private kindsignature kindsignatureField;

  /// <remarks/>
  public kind kind {
    get {
      return this.kindField;
    }
    set {
      this.kindField = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlElementAttribute("kind-signature")]
  public kindsignature kindsignature {
    get {
      return this.kindsignatureField;
    }
    set {
      this.kindsignatureField = value;
    }
  }
}

/// <remarks/>
[System.CodeDom.Compiler.GeneratedCodeAttribute("xsd", "2.0.50727.1432")]
[System.SerializableAttribute()]
[System.Diagnostics.DebuggerStepThroughAttribute()]
[System.ComponentModel.DesignerCategoryAttribute("code")]
[System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true, Namespace = "urn:ietf:params:xml:ns:p2p:config-base")]
[System.Xml.Serialization.XmlRootAttribute(Namespace = "urn:ietf:params:xml:ns:p2p:config-base", IsNullable = false)]
public partial class kind : kindparameter {

  private string nameField;

  private uint idField;

  private bool idFieldSpecified;

  /// <remarks/>
  [System.Xml.Serialization.XmlAttributeAttribute()]
  public string name {
    get {
      return this.nameField;
    }
    set {
      this.nameField = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlAttributeAttribute()]
  public uint id {
    get {
      return this.idField;
    }
    set {
      this.idField = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlIgnoreAttribute()]
  public bool idSpecified {
    get {
      return this.idFieldSpecified;
    }
    set {
      this.idFieldSpecified = value;
    }
  }
}

/// <remarks/>
[System.CodeDom.Compiler.GeneratedCodeAttribute("xsd", "2.0.50727.1432")]
[System.SerializableAttribute()]
[System.Diagnostics.DebuggerStepThroughAttribute()]
[System.ComponentModel.DesignerCategoryAttribute("code")]
[System.Xml.Serialization.XmlTypeAttribute(TypeName = "kind-parameter", Namespace = "urn:ietf:params:xml:ns:p2p:config-base")]
public partial class kindparameter {

  private int maxcountField;

  private bool maxcountFieldSpecified;

  private int maxsizeField;

  private bool maxsizeFieldSpecified;

  private int maxnodemultipleField;

  private bool maxnodemultipleFieldSpecified;

  private string datamodelField;

  private string accesscontrolField;

  private variableresourcenames variableresourcenamesField;

  /// <remarks/>
  [System.Xml.Serialization.XmlElementAttribute("max-count")]
  public int maxcount {
    get {
      return this.maxcountField;
    }
    set {
      this.maxcountField = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlIgnoreAttribute()]
  public bool maxcountSpecified {
    get {
      return this.maxcountFieldSpecified;
    }
    set {
      this.maxcountFieldSpecified = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlElementAttribute("max-size")]
  public int maxsize {
    get {
      return this.maxsizeField;
    }
    set {
      this.maxsizeField = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlIgnoreAttribute()]
  public bool maxsizeSpecified {
    get {
      return this.maxsizeFieldSpecified;
    }
    set {
      this.maxsizeFieldSpecified = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlElementAttribute("max-node-multiple")]
  public int maxnodemultiple {
    get {
      return this.maxnodemultipleField;
    }
    set {
      this.maxnodemultipleField = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlIgnoreAttribute()]
  public bool maxnodemultipleSpecified {
    get {
      return this.maxnodemultipleFieldSpecified;
    }
    set {
      this.maxnodemultipleFieldSpecified = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlElementAttribute("data-model")]
  public string datamodel {
    get {
      return this.datamodelField;
    }
    set {
      this.datamodelField = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlElementAttribute("access-control")]
  public string accesscontrol {
    get {
      return this.accesscontrolField;
    }
    set {
      this.accesscontrolField = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlElementAttribute("variable-resource-names", Namespace = "urn:ietf:params:xml:ns:p2p:config-base:share")]
  public variableresourcenames variableresourcenames {
    get {
      return this.variableresourcenamesField;
    }
    set {
      this.variableresourcenamesField = value;
    }
  }
}

/// <remarks/>
[System.CodeDom.Compiler.GeneratedCodeAttribute("xsd", "2.0.50727.1432")]
[System.SerializableAttribute()]
[System.Diagnostics.DebuggerStepThroughAttribute()]
[System.ComponentModel.DesignerCategoryAttribute("code")]
[System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true, Namespace = "urn:ietf:params:xml:ns:p2p:config-base:share")]
[System.Xml.Serialization.XmlRootAttribute("variable-resource-names", Namespace = "urn:ietf:params:xml:ns:p2p:config-base:share", IsNullable = false)]
public partial class variableresourcenames {

  private string[] patternField;

  private bool enableField;

  /// <remarks/>
  [System.Xml.Serialization.XmlElementAttribute("pattern")]
  public string[] pattern {
    get {
      return this.patternField;
    }
    set {
      this.patternField = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlAttributeAttribute()]
  public bool enable {
    get {
      return this.enableField;
    }
    set {
      this.enableField = value;
    }
  }
}

/// <remarks/>
[System.CodeDom.Compiler.GeneratedCodeAttribute("xsd", "2.0.50727.1432")]
[System.SerializableAttribute()]
[System.Diagnostics.DebuggerStepThroughAttribute()]
[System.ComponentModel.DesignerCategoryAttribute("code")]
[System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true, Namespace = "urn:ietf:params:xml:ns:p2p:config-base")]
[System.Xml.Serialization.XmlRootAttribute("kind-signature", Namespace = "urn:ietf:params:xml:ns:p2p:config-base", IsNullable = false)]
public partial class kindsignature {

  private string algorithmField;

  private string valueField;

  /// <remarks/>
  [System.Xml.Serialization.XmlAttributeAttribute()]
  public string algorithm {
    get {
      return this.algorithmField;
    }
    set {
      this.algorithmField = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlTextAttribute()]
  public string Value {
    get {
      return this.valueField;
    }
    set {
      this.valueField = value;
    }
  }
}

/// <remarks/>
[System.CodeDom.Compiler.GeneratedCodeAttribute("xsd", "2.0.50727.1432")]
[System.SerializableAttribute()]
[System.Diagnostics.DebuggerStepThroughAttribute()]
[System.ComponentModel.DesignerCategoryAttribute("code")]
[System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true, Namespace = "urn:ietf:params:xml:ns:p2p:config-base")]
[System.Xml.Serialization.XmlRootAttribute("self-signed-permitted", Namespace = "urn:ietf:params:xml:ns:p2p:config-base", IsNullable = false)]
public partial class selfsignedpermitted {

  private string digestField;

  private bool valueField;

  /// <remarks/>
  [System.Xml.Serialization.XmlAttributeAttribute()]
  public string digest {
    get {
      return this.digestField;
    }
    set {
      this.digestField = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlTextAttribute()]
  public bool Value {
    get {
      return this.valueField;
    }
    set {
      this.valueField = value;
    }
  }
}

/// <remarks/>
[System.CodeDom.Compiler.GeneratedCodeAttribute("xsd", "2.0.50727.1432")]
[System.SerializableAttribute()]
[System.Diagnostics.DebuggerStepThroughAttribute()]
[System.ComponentModel.DesignerCategoryAttribute("code")]
[System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true, Namespace = "urn:ietf:params:xml:ns:p2p:config-base")]
[System.Xml.Serialization.XmlRootAttribute("bootstrap-node", Namespace = "urn:ietf:params:xml:ns:p2p:config-base", IsNullable = false)]
public partial class bootstrapnode {

  private string addressField;

  private int portField;

  private bool portFieldSpecified;

  /// <remarks/>
  [System.Xml.Serialization.XmlAttributeAttribute()]
  public string address {
    get {
      return this.addressField;
    }
    set {
      this.addressField = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlAttributeAttribute()]
  public int port {
    get {
      return this.portField;
    }
    set {
      this.portField = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlIgnoreAttribute()]
  public bool portSpecified {
    get {
      return this.portFieldSpecified;
    }
    set {
      this.portFieldSpecified = value;
    }
  }
}

/// <remarks/>
[System.CodeDom.Compiler.GeneratedCodeAttribute("xsd", "2.0.50727.1432")]
[System.SerializableAttribute()]
[System.Diagnostics.DebuggerStepThroughAttribute()]
[System.ComponentModel.DesignerCategoryAttribute("code")]
[System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true, Namespace = "urn:ietf:params:xml:ns:p2p:config-base")]
[System.Xml.Serialization.XmlRootAttribute("multicast-bootstrap", Namespace = "urn:ietf:params:xml:ns:p2p:config-base", IsNullable = false)]
public partial class multicastbootstrap {

  private string addressField;

  private int portField;

  private bool portFieldSpecified;

  /// <remarks/>
  [System.Xml.Serialization.XmlAttributeAttribute()]
  public string address {
    get {
      return this.addressField;
    }
    set {
      this.addressField = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlAttributeAttribute()]
  public int port {
    get {
      return this.portField;
    }
    set {
      this.portField = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlIgnoreAttribute()]
  public bool portSpecified {
    get {
      return this.portFieldSpecified;
    }
    set {
      this.portFieldSpecified = value;
    }
  }
}

/// <remarks/>
[System.CodeDom.Compiler.GeneratedCodeAttribute("xsd", "2.0.50727.1432")]
[System.SerializableAttribute()]
[System.Diagnostics.DebuggerStepThroughAttribute()]
[System.ComponentModel.DesignerCategoryAttribute("code")]
[System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true, Namespace = "urn:ietf:params:xml:ns:p2p:config-base:disco")]
[System.Xml.Serialization.XmlRootAttribute(Namespace = "urn:ietf:params:xml:ns:p2p:config-base:disco", IsNullable = false)]
public partial class landmarks {

  private landmarkhost[] landmarkhostField;

  private int versionField;

  /// <remarks/>
  [System.Xml.Serialization.XmlElementAttribute("landmark-host")]
  public landmarkhost[] landmarkhost {
    get {
      return this.landmarkhostField;
    }
    set {
      this.landmarkhostField = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlAttributeAttribute()]
  public int version {
    get {
      return this.versionField;
    }
    set {
      this.versionField = value;
    }
  }
}

/// <remarks/>
[System.CodeDom.Compiler.GeneratedCodeAttribute("xsd", "2.0.50727.1432")]
[System.SerializableAttribute()]
[System.Diagnostics.DebuggerStepThroughAttribute()]
[System.ComponentModel.DesignerCategoryAttribute("code")]
[System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true, Namespace = "urn:ietf:params:xml:ns:p2p:config-base:disco")]
[System.Xml.Serialization.XmlRootAttribute("landmark-host", Namespace = "urn:ietf:params:xml:ns:p2p:config-base:disco", IsNullable = false)]
public partial class landmarkhost {

  private string addressField;

  private int portField;

  /// <remarks/>
  [System.Xml.Serialization.XmlAttributeAttribute()]
  public string address {
    get {
      return this.addressField;
    }
    set {
      this.addressField = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlAttributeAttribute()]
  public int port {
    get {
      return this.portField;
    }
    set {
      this.portField = value;
    }
  }
}

/// <remarks/>
[System.CodeDom.Compiler.GeneratedCodeAttribute("xsd", "2.0.50727.1432")]
[System.SerializableAttribute()]
[System.Diagnostics.DebuggerStepThroughAttribute()]
[System.ComponentModel.DesignerCategoryAttribute("code")]
[System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true, Namespace = "urn:ietf:params:xml:ns:p2p:config-base")]
[System.Xml.Serialization.XmlRootAttribute(Namespace = "urn:ietf:params:xml:ns:p2p:config-base", IsNullable = false)]
public partial class signature {

  private string algorithmField;

  private string valueField;

  /// <remarks/>
  [System.Xml.Serialization.XmlAttributeAttribute()]
  public string algorithm {
    get {
      return this.algorithmField;
    }
    set {
      this.algorithmField = value;
    }
  }

  /// <remarks/>
  [System.Xml.Serialization.XmlTextAttribute()]
  public string Value {
    get {
      return this.valueField;
    }
    set {
      this.valueField = value;
    }
  }
}

/// <remarks/>
[System.CodeDom.Compiler.GeneratedCodeAttribute("xsd", "2.0.50727.1432")]
[System.SerializableAttribute()]
[System.Diagnostics.DebuggerStepThroughAttribute()]
[System.ComponentModel.DesignerCategoryAttribute("code")]
[System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true, Namespace = "urn:ietf:params:xml:ns:p2p:config-base")]
[System.Xml.Serialization.XmlRootAttribute("required-kinds", Namespace = "urn:ietf:params:xml:ns:p2p:config-base", IsNullable = false)]
public partial class requiredkinds {

  private kindblock[] kindblockField;

  /// <remarks/>
  [System.Xml.Serialization.XmlElementAttribute("kind-block")]
  public kindblock[] kindblock {
    get {
      return this.kindblockField;
    }
    set {
      this.kindblockField = value;
    }
  }
}