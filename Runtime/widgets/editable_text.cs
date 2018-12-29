﻿using System;
using System.Collections.Generic;
using RSG;
using Unity.UIWidgets.animation;
using Unity.UIWidgets.foundation;
using Unity.UIWidgets.painting;
using Unity.UIWidgets.rendering;
using Unity.UIWidgets.scheduler;
using Unity.UIWidgets.service;
using Unity.UIWidgets.ui;
using Color = Unity.UIWidgets.ui.Color;
using Rect = Unity.UIWidgets.ui.Rect;
using TextStyle = Unity.UIWidgets.painting.TextStyle;
using Timer = Unity.UIWidgets.async.Timer; 

namespace Unity.UIWidgets.widgets
{
    public delegate void SelectionChangedCallback(TextSelection selection, SelectionChangedCause cause);

    public class TextEditingController : ValueNotifier<TextEditingValue>
    {
        public TextEditingController(string text) : base(text == null
            ? TextEditingValue.empty
            : new TextEditingValue(text))
        {
        }

        private TextEditingController(TextEditingValue value) : base(value ?? TextEditingValue.empty)
        {
        }

        public TextEditingController fromValue(TextEditingValue value)
        {
            return new TextEditingController(value);
        }

        public string text
        {
            get { return value.text; }

            set
            {
                this.value = this.value.copyWith(text: value, selection: TextSelection.collapsed(-1),
                    composing: TextRange.empty);
            }
        }

        public TextSelection selection
        {
            get { return value.selection; }

            set
            {
                if (value.start > text.Length || value.end > text.Length)
                {
                    throw new UIWidgetsError(string.Format("invalid text selection: {0}", value));
                }

                this.value = this.value.copyWith(selection: value, composing: TextRange.empty);
            }
        }

        public void clear()
        {
            value = TextEditingValue.empty;
        }

        public void clearComposing()
        {
            value = value.copyWith(composing: TextRange.empty);
        }
    }

    public class EditableText : StatefulWidget
    {
        public readonly TextEditingController controller;

        public readonly FocusNode focusNode;

        public readonly bool obscureText;

        public readonly bool autocorrect;

        public readonly TextStyle style;

        public readonly TextAlign textAlign;

        public readonly TextDirection? textDirection;

        public readonly double textScaleFactor;

        public readonly Color cursorColor;

        public readonly int maxLines;

        public readonly bool autofocus;

        public readonly Color selectionColor;

        public readonly ValueChanged<string> onChanged;
        public readonly ValueChanged<string> onSubmitted;

        public readonly SelectionChangedCallback onSelectionChanged;

        public readonly List<TextInputFormatter> inputFormatters;

        public readonly bool rendererIgnoresPointer;

        public EditableText(TextEditingController controller, FocusNode focusNode, TextStyle style,
            Color cursorColor, bool obscureText = false, bool autocorrect = false,
            TextAlign textAlign = TextAlign.left, TextDirection? textDirection = null,
            double textScaleFactor = 1.0, int maxLines = 1,
            bool autofocus = false, Color selectionColor = null, ValueChanged<string> onChanged = null,
            ValueChanged<string> onSubmitted = null, SelectionChangedCallback onSelectionChanged = null,
            List<TextInputFormatter> inputFormatters = null, bool rendererIgnoresPointer = false,
            EdgeInsets scrollPadding = null,
            Key key = null) : base(key)
        {
            D.assert(controller != null);
            D.assert(focusNode != null);
            D.assert(style != null);
            D.assert(cursorColor != null);
            this.scrollPadding = scrollPadding ?? EdgeInsets.all(20.0);
            this.controller = controller;
            this.focusNode = focusNode;
            this.obscureText = obscureText;
            this.autocorrect = autocorrect;
            this.style = style;
            this.textAlign = textAlign;
            this.textDirection = textDirection;
            this.textScaleFactor = textScaleFactor;
            this.cursorColor = cursorColor;
            this.maxLines = maxLines;
            this.autofocus = autofocus;
            this.selectionColor = selectionColor;
            this.onChanged = onChanged;
            this.onSubmitted = onSubmitted;
            this.onSelectionChanged = onSelectionChanged;
            this.rendererIgnoresPointer = rendererIgnoresPointer;
            if (maxLines == 1)
            {
                this.inputFormatters = new List<TextInputFormatter>();
                this.inputFormatters.Add(BlacklistingTextInputFormatter.singleLineFormatter);
                if (inputFormatters != null)
                {
                    this.inputFormatters.AddRange(inputFormatters);
                }
            }
            else
            {
                this.inputFormatters = inputFormatters;
            }
        }
        
        public readonly EdgeInsets scrollPadding;

        public override State createState()
        {
            return new EditableTextState();
        }

        public override void debugFillProperties(DiagnosticPropertiesBuilder properties)
        {
            base.debugFillProperties(properties);
            properties.add(new DiagnosticsProperty<TextEditingController>("controller", controller));
            properties.add(new DiagnosticsProperty<FocusNode>("focusNode", focusNode));
            properties.add(new DiagnosticsProperty<bool>("obscureText", obscureText, defaultValue: false));
            properties.add(new DiagnosticsProperty<bool>("autocorrect", autocorrect, defaultValue: true));
            if (style != null)
            {
                style.debugFillProperties(properties);
            }

            properties.add(new EnumProperty<TextAlign>("textAlign", textAlign,
                defaultValue: Diagnostics.kNullDefaultValue));
            properties.add(new EnumProperty<TextDirection?>("textDirection", textDirection,
                defaultValue: Diagnostics.kNullDefaultValue));
            properties.add(new DiagnosticsProperty<double>("textScaleFactor", textScaleFactor,
                defaultValue: Diagnostics.kNullDefaultValue));
            properties.add(new DiagnosticsProperty<int>("maxLines", maxLines, defaultValue: 1));
            properties.add(new DiagnosticsProperty<bool>("autofocus", autofocus, defaultValue: false));
        }
    }

    public class EditableTextState : AutomaticKeepAliveClientMixin<EditableText>, TextInputClient
    {
        const int _kObscureShowLatestCharCursorTicks = 3;
        private static TimeSpan _kCursorBlinkHalfPeriod = TimeSpan.FromMilliseconds(500);
        private Timer _cursorTimer;
        private ValueNotifier<bool> _showCursor = new ValueNotifier<bool>(false);
        private GlobalKey _editableKey = GlobalKey.key();
        private bool _didAutoFocus = false;
        public ScrollController _scrollController = new ScrollController();

        TextInputConnection _textInputConnection;
        private int _obscureShowCharTicksPending = 0;
        private int _obscureLatestCharIndex;

        bool _textChangedSinceLastCaretUpdate = false;

        protected override bool wantKeepAlive
        {
            get { return widget.focusNode.hasFocus; }
        }

        public override void initState()
        {
            base.initState();
            widget.controller.addListener(_didChangeTextEditingValue);
            widget.focusNode.addListener(_handleFocusChanged);
        }

        public override void didChangeDependencies()
        {
            base.didChangeDependencies();
            if (!_didAutoFocus && widget.autofocus)
            {
                FocusScope.of(context).autofocus(widget.focusNode);
                _didAutoFocus = true;
            }
        }

        public override void didUpdateWidget(StatefulWidget old)
        {
            EditableText oldWidget = (EditableText) old;
            base.didUpdateWidget(oldWidget);
            if (widget.controller != oldWidget.controller)
            {
                oldWidget.controller.removeListener(_didChangeTextEditingValue);
                widget.controller.addListener(_didChangeTextEditingValue);
                _updateRemoteEditingValueIfNeeded();
            }

            if (widget.focusNode != oldWidget.focusNode)
            {
                oldWidget.focusNode.removeListener(_handleFocusChanged);
                widget.focusNode.addListener(_handleFocusChanged);
                updateKeepAlive();
            }
        }

        public override void dispose()
        {
            widget.controller.removeListener(_didChangeTextEditingValue);
            _closeInputConnectionIfNeeded();
            D.assert(!_hasInputConnection);
            _stopCursorTimer();
            D.assert(_cursorTimer == null);
            widget.focusNode.removeListener(_handleFocusChanged);
            base.dispose();
        }

        TextEditingValue _lastKnownRemoteTextEditingValue;

        public void updateEditingValue(TextEditingValue value)
        {
            if (value.text != _value.text)
            {
                // _hideSelectionOverlayIfNeeded();
                if (widget.obscureText && value.text.Length == _value.text.Length + 1)
                {
                    _obscureShowCharTicksPending = _kObscureShowLatestCharCursorTicks;
                    _obscureLatestCharIndex = _value.selection.baseOffset;
                }
            }

            _lastKnownRemoteTextEditingValue = value;
            _formatAndSetValue(value);
        }

        public TextEditingValue getValueForOperation(TextEditOp operation)
        {
            TextPosition newPosition = null;
            TextPosition newExtend = null;
            TextEditingValue newValue = null;
            TextSelection newSelection = null;
            TextPosition startPos = new TextPosition(_value.selection.start, _value.selection.affinity);
            switch (operation)
            {
                case TextEditOp.MoveLeft:
                    newValue = _value.moveLeft();
                    break;
                case TextEditOp.MoveRight:
                    newValue = _value.moveRight();
                    break;
                case TextEditOp.MoveUp:
                    newPosition = this.renderEditable.getPositionUp(startPos);
                    break;
                case TextEditOp.MoveDown:
                    newPosition = this.renderEditable.getPositionDown(startPos);
                    break;
                case TextEditOp.MoveLineStart:
                    newPosition = this.renderEditable.getParagraphStart(startPos, TextAffinity.downstream);
                    break;
                case TextEditOp.MoveLineEnd:
                    newPosition = this.renderEditable.getParagraphEnd(startPos, TextAffinity.upstream);
                    break;
                case TextEditOp.MoveWordRight:
                    newPosition = this.renderEditable.getWordRight(startPos);
                    break;
                case TextEditOp.MoveWordLeft:
                    newPosition = this.renderEditable.getWordLeft(startPos);
                    break;
//                case TextEditOp.MoveToStartOfNextWord:      MoveToStartOfNextWord(); break;
//                case TextEditOp.MoveToEndOfPreviousWord:        MoveToEndOfPreviousWord(); break;
                case TextEditOp.MoveTextStart:
                    newPosition = new TextPosition(0);
                    break;
                case TextEditOp.MoveTextEnd:
                    newPosition = new TextPosition(_value.text.Length);
                    break;
                case TextEditOp.MoveParagraphForward:
                    newPosition = this.renderEditable.getParagraphForward(startPos);
                    break;
                case TextEditOp.MoveParagraphBackward:
                    newPosition = this.renderEditable.getParagraphBackward(startPos);
                    break;
                case TextEditOp.MoveGraphicalLineStart:
                    newPosition = this.renderEditable.getLineStartPosition(startPos, TextAffinity.downstream);
                    break;
                case TextEditOp.MoveGraphicalLineEnd:
                    newPosition = this.renderEditable.getLineEndPosition(startPos, TextAffinity.upstream);
                    break;
                case TextEditOp.SelectLeft:
                    newValue = _value.extendLeft();
                    break;
                case TextEditOp.SelectRight:
                    newValue = _value.extendRight();
                    break;
                case TextEditOp.SelectUp:
                    newExtend = this.renderEditable.getPositionUp(_value.selection.extendPos);
                    break;
                case TextEditOp.SelectDown:
                    newExtend = this.renderEditable.getPositionDown(_value.selection.extendPos);
                    break;
                case TextEditOp.SelectWordRight:
                    newExtend = this.renderEditable.getWordRight(_value.selection.extendPos);
                    break;
                case TextEditOp.SelectWordLeft:
                    newExtend = this.renderEditable.getWordLeft(_value.selection.extendPos);
                    break;
//                case TextEditOp.SelectToEndOfPreviousWord:  SelectToEndOfPreviousWord(); break;
//                case TextEditOp.SelectToStartOfNextWord:    SelectToStartOfNextWord(); break;
//
                case TextEditOp.SelectTextStart:
                    newExtend = new TextPosition(0);
                    break;
                case TextEditOp.SelectTextEnd:
                    newExtend = new TextPosition(_value.text.Length);
                    break;
               case TextEditOp.ExpandSelectGraphicalLineStart:
                   if (_value.selection.isCollapsed || !this.renderEditable.isLineEndOrStart(_value.selection.start))
                   {
                       newSelection = new TextSelection(this.renderEditable.getLineStartPosition(startPos).offset, 
                           _value.selection.end, _value.selection.affinity);
                   }
                  
                   break;
                case TextEditOp.ExpandSelectGraphicalLineEnd:
                    if (_value.selection.isCollapsed || !this.renderEditable.isLineEndOrStart(_value.selection.end))
                    {
                        newSelection = new TextSelection(_value.selection.start, 
                            this.renderEditable.getLineEndPosition(_value.selection.endPos).offset,
                            _value.selection.affinity);
                    }
                    break;
                case TextEditOp.SelectParagraphForward:
                    newExtend = this.renderEditable.getParagraphForward(_value.selection.extendPos);
                    break;
                case TextEditOp.SelectParagraphBackward:
                    newExtend = this.renderEditable.getParagraphBackward(_value.selection.extendPos);
                    break;
                case TextEditOp.SelectGraphicalLineStart:
                    newExtend = this.renderEditable.getLineStartPosition(_value.selection.extendPos);
                    break;
                case TextEditOp.SelectGraphicalLineEnd:
                    newExtend = this.renderEditable.getLineEndPosition(startPos);
                    break;
                case TextEditOp.Delete:
                    newValue = _value.deleteSelection(false);
                    break;
                case TextEditOp.Backspace:
                    newValue = _value.deleteSelection();
                    break;
                case TextEditOp.SelectAll:
                    newSelection = _value.selection.copyWith(baseOffset: 0, extentOffset: _value.text.Length);
                    break;
            }

            if (newPosition != null)
            {
                return _value.copyWith(selection: TextSelection.fromPosition(newPosition));
            }
            else if (newExtend != null)
            {
                return _value.copyWith(selection: _value.selection.copyWith(extentOffset: newExtend.offset));
            } else if (newSelection != null)
            {
                return _value.copyWith(selection: newSelection);
            }
            else if (newValue != null)
            {
                return newValue;
            }

            return _value;
        }

        void _updateRemoteEditingValueIfNeeded()
        {
            if (!_hasInputConnection)
                return;
            var localValue = _value;
            if (localValue == _lastKnownRemoteTextEditingValue)
                return;
            _lastKnownRemoteTextEditingValue = localValue;
            _textInputConnection.setEditingState(localValue);
        }

        // Calculate the new scroll offset so the cursor remains visible.
        double _getScrollOffsetForCaret(Rect caretRect)
        {
            double caretStart = _isMultiline ? caretRect.top : caretRect.left;
            double caretEnd = _isMultiline ? caretRect.bottom : caretRect.right;
            double scrollOffset = _scrollController.offset;
            double viewportExtent = _scrollController.position.viewportDimension;
            if (caretStart < 0.0)
            {
                scrollOffset += caretStart;
            } else if (caretEnd >= viewportExtent)
            {
                scrollOffset += caretEnd - viewportExtent;
            }

            return scrollOffset;
        }

        // Calculates where the `caretRect` would be if `_scrollController.offset` is set to `scrollOffset`.
        Rect _getCaretRectAtScrollOffset(Rect caretRect, double scrollOffset) {
            double offsetDiff = _scrollController.offset - scrollOffset;
            return _isMultiline ? caretRect.translate(0.0, offsetDiff) : caretRect.translate(offsetDiff, 0.0);
        }
        
        bool _hasInputConnection
        {
            get { return _textInputConnection != null && _textInputConnection.attached; }
        }


        void _openInputConnection()
        {
            if (!_hasInputConnection)
            {
                TextEditingValue localValue = _value;
                _lastKnownRemoteTextEditingValue = localValue;
                _textInputConnection = Window.instance.textInput.attach(this);
                _textInputConnection.setEditingState(localValue);
            }
        }

        void _closeInputConnectionIfNeeded()
        {
            if (_hasInputConnection)
            {
                _textInputConnection.close();
                _textInputConnection = null;
                _lastKnownRemoteTextEditingValue = null;
            }
        }

        void _openOrCloseInputConnectionIfNeeded()
        {
            if (_hasFocus && widget.focusNode.consumeKeyboardToken())
            {
                _openInputConnection();
            }
            else if (!_hasFocus)
            {
                _closeInputConnectionIfNeeded();
                widget.controller.clearComposing();
            }
        }

        public void requestKeyboard()
        {
            if (_hasFocus)
            {
                _openInputConnection();
            }
            else
            {
                FocusScope.of(context).requestFocus(widget.focusNode);
            }
        }

        private void _handleSelectionChanged(TextSelection selection, RenderEditable renderObject,
            SelectionChangedCause cause)
        {
            widget.controller.selection = selection;
            requestKeyboard();

            if (widget.onSelectionChanged != null)
            {
                widget.onSelectionChanged(selection, cause);
            }
        }

        Rect _currentCaretRect;
        void _handleCaretChanged(Rect caretRect)
        {
            _currentCaretRect = caretRect;
            // If the caret location has changed due to an update to the text or
            // selection, then scroll the caret into view.
            if (_textChangedSinceLastCaretUpdate)
            {
                _textChangedSinceLastCaretUpdate = false;
                _showCaretOnScreen();
            }
        }

        // Animation configuration for scrolling the caret back on screen.
        static readonly TimeSpan _caretAnimationDuration = TimeSpan.FromMilliseconds(100);
        static readonly Curve _caretAnimationCurve = Curves.fastOutSlowIn;
        bool _showCaretOnScreenScheduled = false;
        
        void _showCaretOnScreen()
        {
            if (_showCaretOnScreenScheduled)
            {
                return;
            }

            _showCaretOnScreenScheduled = true;
            SchedulerBinding.instance.addPostFrameCallback(_ =>
            {
                _showCaretOnScreenScheduled = false;
                if (_currentCaretRect == null || !_scrollController.hasClients)
                {
                    return;
                }
                double scrollOffsetForCaret =  _getScrollOffsetForCaret(_currentCaretRect);
                _scrollController.animateTo(scrollOffsetForCaret, duration: _caretAnimationDuration,
                    curve: _caretAnimationCurve);
                
                Rect newCaretRect = _getCaretRectAtScrollOffset(_currentCaretRect, scrollOffsetForCaret);
                // Enlarge newCaretRect by scrollPadding to ensure that caret is not positioned directly at the edge after scrolling.
                Rect inflatedRect = Rect.fromLTRB(
                    newCaretRect.left - widget.scrollPadding.left,
                    newCaretRect.top - widget.scrollPadding.top,
                    newCaretRect.right + widget.scrollPadding.right,
                    newCaretRect.bottom + widget.scrollPadding.bottom
                );
                _editableKey.currentContext.findRenderObject().showOnScreen(
                    rect: inflatedRect,
                    duration: _caretAnimationDuration,
                    curve: _caretAnimationCurve
                );
            });
        }
        
        private void _formatAndSetValue(TextEditingValue value)
        {
            var textChanged = (_value == null ? null : _value.text) != (value == null ? null : value.text);
            if (widget.inputFormatters != null && widget.inputFormatters.isNotEmpty())
            {
                foreach (var formatter in widget.inputFormatters)
                {
                    value = formatter.formatEditUpdate(_value, value);
                }

                _value = value;
                _updateRemoteEditingValueIfNeeded();
            }
            else
            {
                _value = value;
            }

            if (textChanged && widget.onChanged != null)
            {
                widget.onChanged(value.text);
            }
        }

        public bool cursorCurrentlyVisible
        {
            get { return _showCursor.value; }
        }

        public TimeSpan cursorBlinkInterval
        {
            get { return _kCursorBlinkHalfPeriod; }
        }
        
        private void _cursorTick() {
            _showCursor.value = !_showCursor.value;
            if (_obscureShowCharTicksPending > 0) {
                setState(() => { _obscureShowCharTicksPending--;});
            }
        }

        private void _startCursorTimer() {
            _showCursor.value = true;
            _cursorTimer = Window.instance.run(_kCursorBlinkHalfPeriod, _cursorTick, periodic:true);
        }
        
        private void _stopCursorTimer() {
            if (_cursorTimer != null)
            {
                _cursorTimer.cancel();    
            }
            _cursorTimer = null;
            _showCursor.value = false;
            _obscureShowCharTicksPending = 0;
        }
        
        private void _startOrStopCursorTimerIfNeeded() {
            if (_cursorTimer == null && _hasFocus && _value.selection.isCollapsed)
            {
                _startCursorTimer();
            }
            else if (_cursorTimer != null && (!_hasFocus || !_value.selection.isCollapsed))
            {
                _stopCursorTimer();
            }
        }
        
        private TextEditingValue _value
        {
            get { return widget.controller.value; }
            set { widget.controller.value = value; }
        }

        private bool _hasFocus
        {
            get { return widget.focusNode.hasFocus; }
        }

        private bool _isMultiline
        {
            get { return widget.maxLines != 1; }
        }

        private void _didChangeTextEditingValue()
        {
            _updateRemoteEditingValueIfNeeded();
            _startOrStopCursorTimerIfNeeded();
            _textChangedSinceLastCaretUpdate = true;
            setState(() => { });
        }

        private void _handleFocusChanged()
        {
            _openOrCloseInputConnectionIfNeeded();
            _startOrStopCursorTimerIfNeeded();
            if (!_hasFocus)
            {
                _value = new TextEditingValue(text: _value.text);
            }
            else if (!_value.selection.isValid)
            {
                widget.controller.selection = TextSelection.collapsed(offset: _value.text.Length);
            }

            updateKeepAlive();
        }


        private TextDirection? _textDirection
        {
            get
            {
                TextDirection? result = widget.textDirection ?? Directionality.of(context);
                D.assert(result != null,
                    string.Format("{0} created without a textDirection and with no ambient Directionality.",
                        GetType().FullName));
                return result;
            }
        }

        public RenderEditable renderEditable
        {
            get { return (RenderEditable) _editableKey.currentContext.findRenderObject(); }
        }

        public override Widget build(BuildContext context)
        {
            FocusScope.of(context).reparentIfNeeded(widget.focusNode);
            base.build(context); // See AutomaticKeepAliveClientMixin.
            
            return new Scrollable(
                axisDirection: _isMultiline ? AxisDirection.down : AxisDirection.right,
                controller: _scrollController,
                physics: new ClampingScrollPhysics(),
                viewportBuilder: (BuildContext _context, ViewportOffset offset) =>
                    new _Editable(
                        key: _editableKey,
                        textSpan: buildTextSpan(),
                        value: _value,
                        cursorColor: widget.cursorColor,
                        showCursor: _showCursor,
                        hasFocus: _hasFocus,
                        maxLines: widget.maxLines,
                        selectionColor: widget.selectionColor,
                        textScaleFactor: Window.instance.devicePixelRatio, // todo widget.textScaleFactor ?? MediaQuery.textScaleFactorOf(context),
                        textAlign: widget.textAlign,
                        textDirection: _textDirection,
                        obscureText: widget.obscureText,
                        autocorrect: widget.autocorrect,
                        offset: offset,
                        onSelectionChanged: _handleSelectionChanged,
                        onCaretChanged: _handleCaretChanged,
                        rendererIgnoresPointer: widget.rendererIgnoresPointer
                    )
                
                );
        }

        public TextSpan buildTextSpan()
        {
            if (!widget.obscureText && _value.composing.isValid)
            {
                TextStyle composingStyle = widget.style.merge(
                    new TextStyle(decoration: TextDecoration.underline)
                );

                return new TextSpan(
                    style: widget.style,
                    children: new List<TextSpan>
                    {
                        new TextSpan(text: _value.composing.textBefore(_value.text)),
                        new TextSpan(
                            style: composingStyle,
                            text: _value.composing.textInside(_value.text)
                        ),
                        new TextSpan(text: _value.composing.textAfter(_value.text)),
                    });
            }

            var text = _value.text;
            if (widget.obscureText)
            {
                text = new string(RenderEditable.obscuringCharacter, text.Length);
                int o =
                    _obscureShowCharTicksPending > 0 ? _obscureLatestCharIndex : -1;
                if (o >= 0 && o < text.Length)
                    text = text.Substring(0, o) + _value.text.Substring(o, 1) + text.Substring(o + 1);
            }

            return new TextSpan(style: widget.style, text: text);
        }
    }


    internal class _Editable : LeafRenderObjectWidget
    {
        public readonly TextSpan textSpan;
        public readonly TextEditingValue value;
        public readonly Color cursorColor;
        public readonly ValueNotifier<bool> showCursor;
        public readonly bool hasFocus;
        public readonly int maxLines;
        public readonly Color selectionColor;
        public readonly double textScaleFactor;
        public readonly TextAlign textAlign;
        public readonly TextDirection? textDirection;
        public readonly bool obscureText;
        public readonly bool autocorrect;
        public readonly ViewportOffset offset;
        public readonly SelectionChangedHandler onSelectionChanged;
        public readonly CaretChangedHandler onCaretChanged;
        public readonly bool rendererIgnoresPointer;


        public _Editable(TextSpan textSpan = null, TextEditingValue value = null,
            Color cursorColor = null, ValueNotifier<bool> showCursor = null, bool hasFocus = false,
            int maxLines = 0, Color selectionColor = null, double textScaleFactor = 1.0,
            TextDirection? textDirection = null, bool obscureText = false, TextAlign textAlign = TextAlign.left,
            bool autocorrect = false, ViewportOffset offset = null, SelectionChangedHandler onSelectionChanged = null,
            CaretChangedHandler onCaretChanged = null, bool rendererIgnoresPointer = false, Key key = null) : base(key)
        {
            this.textSpan = textSpan;
            this.value = value;
            this.cursorColor = cursorColor;
            this.showCursor = showCursor;
            this.hasFocus = hasFocus;
            this.maxLines = maxLines;
            this.selectionColor = selectionColor;
            this.textScaleFactor = textScaleFactor;
            this.textAlign = textAlign;
            this.textDirection = textDirection;
            this.obscureText = obscureText;
            this.autocorrect = autocorrect;
            this.offset = offset;
            this.onSelectionChanged = onSelectionChanged;
            this.onCaretChanged = onCaretChanged;
            this.rendererIgnoresPointer = rendererIgnoresPointer;
        }

        public override RenderObject createRenderObject(BuildContext context)
        {
            return new RenderEditable(
                text: textSpan,
                textDirection: textDirection ?? TextDirection.ltr,
                offset: offset,
                showCursor: showCursor,
                cursorColor: cursorColor,
                hasFocus: hasFocus,
                maxLines: maxLines,
                selectionColor: selectionColor,
                textScaleFactor: textScaleFactor,
                textAlign: textAlign,
                selection: value.selection,
                obscureText: obscureText,
                onSelectionChanged: onSelectionChanged,
                onCaretChanged: onCaretChanged,
                ignorePointer: rendererIgnoresPointer
            );
        }

        public override void updateRenderObject(BuildContext context, RenderObject renderObject)
        {
            var edit = (RenderEditable) renderObject;
            edit.text = textSpan;
            edit.cursorColor = cursorColor;
            edit.showCursor = showCursor;
            edit.hasFocus = hasFocus;
            edit.maxLines = maxLines;
            edit.selectionColor = selectionColor;
            edit.textScaleFactor = textScaleFactor;
            edit.textAlign = textAlign;
            edit.textDirection = textDirection;
            edit.selection = value.selection;
            edit.offset = offset;
            edit.onSelectionChanged = onSelectionChanged;
            edit.onCaretChanged = onCaretChanged;
            edit.ignorePointer = rendererIgnoresPointer;
            edit.obscureText = obscureText;
        }
    }
}